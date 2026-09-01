using BusinessServices.Interface;

namespace BusinessServices.Implementation
{
    public class NullInstanceTrackerService : IInstanceTrackerService
    {
        public Task RegisterAsync(string instanceId) => Task.CompletedTask;
        public Task HeartbeatAsync(string instanceId) => Task.CompletedTask;
        public Task UnregisterAsync(string instanceId) => Task.CompletedTask;
        public Task<int> GetActiveCountAsync() => Task.FromResult(0);
        public Task<long> GetTodayStartedAsync() => Task.FromResult(0L);
        public Task<InstanceTrackerStats> GetStatsAsync()
        {
            var today = DateTime.UtcNow.AddHours(5.5);
            return Task.FromResult(new InstanceTrackerStats(
                0, Array.Empty<InstanceInfo>(), 0, 0,
                today.ToString("yyyy-MM-dd"), 0,
                today.AddDays(-1).ToString("yyyy-MM-dd"),
                Array.Empty<InstanceHistoryEntry>()));
        }
    }
}
