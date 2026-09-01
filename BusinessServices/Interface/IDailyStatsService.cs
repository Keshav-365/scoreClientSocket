namespace BusinessServices.Interface
{
    public record DailyStats(string Date, long Clients, long Upstream);

    public interface IDailyStatsService
    {
        Task IncrClientAsync();
        Task IncrUpstreamAsync();
        Task<DailyStats> GetAsync(DateOnly date);
    }
}
