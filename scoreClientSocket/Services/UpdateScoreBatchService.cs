using BusinessServices.Implementation;
using BusinessServices.Interface;
using Modal;

namespace scoreClientSocket.Services
{
    // Replaces the per-client OnUpdateScore fan-out with a single batched call.
    // getupdateScore() only stamps lastconnectiontime in cache; this service
    // reads those stamps every IntervalSeconds, collects event IDs that had an
    // active client within the window, and sends ONE OnUpdateScore to ScoreProvider.
    public class UpdateScoreBatchService : BackgroundService
    {
        private readonly getScore _gs;
        private readonly ILocalEventCacheService _cache;

        public UpdateScoreBatchService(getScore gs, ILocalEventCacheService cache)
        {
            _gs = gs;
            _cache = cache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int interval = AppCache.Settings.UpdateScoreBatch.IntervalSeconds;
                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);

                try
                {
                    var cutoff = common.GetDateTime().AddSeconds(-interval);

                    var activeIds = _cache.GetAll()
                        .Where(e => e.lastconnectiontime >= cutoff)
                        .Select(e => e.eventid.ToString())
                        .ToList();

                    if (activeIds.Count > 0)
                        await _gs.NotifyUpstream("OnUpdateScore", string.Join(",", activeIds));
                }
                catch { }
            }
        }
    }
}
