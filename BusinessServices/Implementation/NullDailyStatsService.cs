using BusinessServices.Interface;

namespace BusinessServices.Implementation
{
    public class NullDailyStatsService : IDailyStatsService
    {
        public Task IncrClientAsync() => Task.CompletedTask;
        public Task IncrUpstreamAsync() => Task.CompletedTask;
        public Task<DailyStats> GetAsync(DateOnly date)
            => Task.FromResult(new DailyStats(date.ToString("yyyy-MM-dd"), 0, 0));
    }
}
