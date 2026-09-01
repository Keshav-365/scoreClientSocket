namespace BusinessServices.Interface
{
    public record InstanceInfo(string Id, string StartedAt);

    // One row of instance lifecycle history. DestroyedAt is null while the
    // instance is still running; set to the shutdown/crash time once it ends.
    public record InstanceHistoryEntry(
        string Id,
        string StartedAt,
        string? DestroyedAt,
        string? DestroyReason
    );

    public record InstanceTrackerStats(
        int ActiveCount,
        IReadOnlyList<InstanceInfo> ActiveInstances,
        long TotalStarted,
        long TodayStarted,
        string TodayDate,
        long YesterdayStarted,
        string YesterdayDate,
        IReadOnlyList<InstanceHistoryEntry> RecentInstances
    );

    public interface IInstanceTrackerService
    {
        Task RegisterAsync(string instanceId);
        Task HeartbeatAsync(string instanceId);
        Task UnregisterAsync(string instanceId);
        Task<InstanceTrackerStats> GetStatsAsync();
        Task<int> GetActiveCountAsync();
        Task<long> GetTodayStartedAsync();
    }
}
