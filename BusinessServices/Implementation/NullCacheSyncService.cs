using BusinessServices.Interface;

namespace BusinessServices.Implementation
{
    public class NullCacheSyncService : ICacheSyncService
    {
        public Task PublishClearAllAsync() => Task.CompletedTask;
        public Task PublishClearEventAsync(int eventId) => Task.CompletedTask;
    }
}
