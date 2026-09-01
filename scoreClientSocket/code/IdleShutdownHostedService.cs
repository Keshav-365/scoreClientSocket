using BusinessServices.Interface;
using Modal;

namespace scoreClientSocket.code
{
    // Forcefully (but gracefully) stops this instance when it has served ZERO end-client
    // connections for a sustained period, so idle Cloud Run instances drain instead of
    // lingering. Stopping runs the normal shutdown path (InstanceTrackerHostedService
    // unregisters the instance, recording destroyedAt with reason "idle").
    //
    // Opt-in via IdleShutdown.Enabled. Guards against restart-thrashing:
    //  • StartupGraceSeconds — a fresh instance always starts at 0 connections, so it is not
    //    eligible to stop until it has been up this long.
    //  • IdleTimeoutSeconds  — connections must stay at 0 continuously for this long; a single
    //    new connection resets the clock.
    //  • MinActiveInstances  — never drains below this many active instances (keeps N warm).
    //    The active count always reads as 0 (no cross-instance tracking), so this
    //    check never actually blocks a stop.
    //  • Jitter — a random extra wait de-synchronises simultaneous shutdowns across replicas,
    //    and connection/active counts are re-verified after the wait before committing.
    public class IdleShutdownHostedService : BackgroundService
    {
        private readonly IActiveConnectionCountService _connCount;
        private readonly IInstanceTrackerService _tracker;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<IdleShutdownHostedService> _logger;
        private readonly string _instanceId;

        public IdleShutdownHostedService(
            IActiveConnectionCountService connCount,
            IInstanceTrackerService tracker,
            IHostApplicationLifetime lifetime,
            ILogger<IdleShutdownHostedService> logger)
        {
            _connCount  = connCount;
            _tracker    = tracker;
            _lifetime   = lifetime;
            _logger     = logger;
            _instanceId = AppCache.InstanceId;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var cfg = AppCache.Settings.IdleShutdown;
            if (cfg is null || !cfg.Enabled) return;

            _logger.LogWarning(
                "IdleShutdown | ENABLED | idleTimeout={Idle}s grace={Grace}s minInstances={Min} | instance={Instance}",
                cfg.IdleTimeoutSeconds, cfg.StartupGraceSeconds, cfg.MinActiveInstances, _instanceId);

            var bootAt = DateTime.UtcNow;
            DateTime? idleSince = null;
            int checkInterval = Math.Max(5, cfg.CheckIntervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(checkInterval), stoppingToken); }
                catch (OperationCanceledException) { break; }
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    // Startup grace: never stop a just-booted instance (0 connections is expected).
                    if ((DateTime.UtcNow - bootAt).TotalSeconds < cfg.StartupGraceSeconds)
                        continue;

                    long local = await _connCount.GetLocalAsync();
                    if (local > 0)
                    {
                        idleSince = null;   // busy — reset the idle clock
                        continue;
                    }

                    // local == 0 → start / continue the idle clock.
                    idleSince ??= DateTime.UtcNow;
                    if ((DateTime.UtcNow - idleSince.Value).TotalSeconds < cfg.IdleTimeoutSeconds)
                        continue;

                    // Idle long enough — but don't drain below the configured warm floor.
                    int active = await _tracker.GetActiveCountAsync();
                    if (active <= cfg.MinActiveInstances)
                    {
                        _logger.LogInformation(
                            "IdleShutdown | idle {Idle}s but holding warm floor | active={Active} min={Min} | instance={Instance}",
                            (int)(DateTime.UtcNow - idleSince.Value).TotalSeconds, active, cfg.MinActiveInstances, _instanceId);
                        continue;
                    }

                    // Jitter so multiple idle replicas don't all exit at once, then re-verify.
                    int jitter = cfg.MaxJitterSeconds > 0 ? Random.Shared.Next(cfg.MaxJitterSeconds + 1) : 0;
                    if (jitter > 0)
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(jitter), stoppingToken); }
                        catch (OperationCanceledException) { break; }
                    }

                    if (await _connCount.GetLocalAsync() > 0)
                    {
                        idleSince = null;   // a client arrived during the jitter window — abort
                        continue;
                    }
                    if (await _tracker.GetActiveCountAsync() <= cfg.MinActiveInstances)
                        continue;           // another replica already drained — stay up

                    _logger.LogWarning(
                        "IdleShutdown | STOPPING idle instance | idleFor={Idle}s active={Active} | instance={Instance} | time={Time}",
                        (int)(DateTime.UtcNow - idleSince.Value).TotalSeconds, active, _instanceId, common.GetDateTime());

                    AppCache.ShutdownReason = "idle";
                    _lifetime.StopApplication();   // graceful stop → unregister records destroyedAt="idle"
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "IdleShutdown | check failed | instance={Instance}", _instanceId);
                }
            }
        }
    }
}
