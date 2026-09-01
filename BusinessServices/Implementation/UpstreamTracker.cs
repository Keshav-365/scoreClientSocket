namespace BusinessServices.Implementation
{
    // Singleton that remembers the SignalR connection IDs of ScoreProvider instances
    // that have registered as upstream data sources via RegisterAsUpstream().
    // Async overloads are provided so callers never block a thread-pool thread on I/O;
    // these in-memory versions just wrap the sync result in a completed Task.
    public class UpstreamTracker
    {
        private readonly HashSet<string> _upstreamIds = new();
        private readonly object _lock = new();

        public void Add(string connectionId)
        {
            lock (_lock) { _upstreamIds.Add(connectionId); }
        }

        public void Remove(string connectionId)
        {
            lock (_lock) { _upstreamIds.Remove(connectionId); }
        }

        public IReadOnlyList<string> GetAll()
        {
            lock (_lock) { return _upstreamIds.ToList(); }
        }

        public bool IsUpstream(string connectionId)
        {
            lock (_lock) { return _upstreamIds.Contains(connectionId); }
        }

        public virtual Task AddAsync(string connectionId) { Add(connectionId); return Task.CompletedTask; }
        public virtual Task RemoveAsync(string connectionId) { Remove(connectionId); return Task.CompletedTask; }
        public virtual Task<IReadOnlyList<string>> GetAllAsync() => Task.FromResult(GetAll());
        public virtual Task<bool> IsUpstreamAsync(string connectionId) => Task.FromResult(IsUpstream(connectionId));
    }
}
