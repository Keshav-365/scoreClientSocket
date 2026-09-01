namespace BusinessServices.Interface
{
    public interface ICacheSyncService
    {
        Task PublishClearAllAsync();
        Task PublishClearEventAsync(int eventId);
    }
}
