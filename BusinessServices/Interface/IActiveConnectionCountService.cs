namespace BusinessServices.Interface
{
    public interface IActiveConnectionCountService
    {
        string InstanceId { get; }

        // Hot path — updates the local ConcurrentDictionary immediately.
        Task AddConnectionAsync(string connectionId);
        Task RemoveConnectionAsync(string connectionId);

        // Local = real-time from ConcurrentDictionary.
        // Global = same as Local; there is no cross-instance aggregation without a shared store.
        Task<long> GetLocalAsync();
        Task<long> GetGlobalAsync();
    }
}
