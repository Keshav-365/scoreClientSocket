using BusinessServices.Interface;
using Modal;
using System.Collections.Concurrent;

namespace BusinessServices.Implementation
{
    public class LocalActiveConnectionCountService : IActiveConnectionCountService
    {
        public string InstanceId { get; } = AppCache.InstanceId;

        private readonly ConcurrentDictionary<string, byte> _localIds = new();

        public Task AddConnectionAsync(string connectionId)
        {
            _localIds.TryAdd(connectionId, 0);
            return Task.CompletedTask;
        }

        public Task RemoveConnectionAsync(string connectionId)
        {
            _localIds.TryRemove(connectionId, out _);
            return Task.CompletedTask;
        }

        public Task<long> GetLocalAsync()
            => Task.FromResult((long)_localIds.Count);

        public Task<long> GetGlobalAsync()
            => Task.FromResult((long)_localIds.Count);
    }
}
