using BusinessServices.Interface;
using Modal;

namespace scoreprovidersocket.code
{
    // Registers this Cloud Run instance with IInstanceTrackerService on startup, sends a
    // heartbeat every 30s, and unregisters on graceful shutdown. With no shared store
    // configured this is a no-op (NullInstanceTrackerService) and instance stats always
    // read as zero/empty — single-instance deployments simply don't track this.
    public class InstanceTrackerHostedService : BackgroundService
    {
        private readonly IInstanceTrackerService _tracker;
        private readonly ILogger<InstanceTrackerHostedService> _logger;
        private readonly string _instanceId;

        private const int HeartbeatIntervalSeconds = 30;

        public InstanceTrackerHostedService(
            IInstanceTrackerService tracker,
            ILogger<InstanceTrackerHostedService> logger)
        {
            _tracker    = tracker;
            _logger     = logger;
            _instanceId = AppCache.InstanceId;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _tracker.RegisterAsync(_instanceId);

                _logger.LogWarning(
                    "Instance REGISTERED | instanceId={InstanceId} | time={Time}",
                    _instanceId, common.GetDateTime());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Instance registration failed | instanceId={InstanceId} | time={Time}",
                    _instanceId, common.GetDateTime());
            }

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), stoppingToken)
                          .ContinueWith(_ => { });   // swallow OperationCancelled on shutdown

                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    await _tracker.HeartbeatAsync(_instanceId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Instance heartbeat failed | instanceId={InstanceId} | time={Time}",
                        _instanceId, common.GetDateTime());
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _tracker.UnregisterAsync(_instanceId);

                _logger.LogWarning(
                    "Instance UNREGISTERED (graceful shutdown) | instanceId={InstanceId} | time={Time}",
                    _instanceId, common.GetDateTime());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Instance unregistration failed | instanceId={InstanceId} | time={Time}",
                    _instanceId, common.GetDateTime());
            }

            await base.StopAsync(cancellationToken);
        }
    }
}
