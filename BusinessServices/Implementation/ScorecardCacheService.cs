using BusinessServices.Interface;
using Modal;
using System.Collections.Concurrent;

namespace BusinessServices.Implementation
{
    public class ScorecardCacheService : IScorecardCacheService
    {
        private static TimeSpan Ttl =>
            TimeSpan.FromMinutes(AppCache.Settings.ScorecardCache.LinkTtlMinutes);

        // Key: "{eventId}_{link}_{pid}" — uniquely identifies a scorecard URL.
        // Color and font are decorators applied by the caller, not stored here.
        private readonly ConcurrentDictionary<string, (ScoreResponse Response, DateTime ExpiresAt)> _cache = new();

        private static string Key(string eventId, int link, int pid) => $"{eventId}_{link}_{pid}";

        public ScoreResponse? Get(string eventId, int link, int pid)
        {
            if (!_cache.TryGetValue(Key(eventId, link, pid), out var entry)) return null;
            if (DateTime.UtcNow >= entry.ExpiresAt)
            {
                _cache.TryRemove(Key(eventId, link, pid), out _);
                return null;
            }
            return entry.Response;
        }

        public void Set(string eventId, int link, int pid, ScoreResponse response) =>
            _cache[Key(eventId, link, pid)] = (response, DateTime.UtcNow.Add(Ttl));

        public void Remove(string eventId)
        {
            var prefix = eventId + "_";
            foreach (var key in _cache.Keys.Where(k => k.StartsWith(prefix)))
                _cache.TryRemove(key, out _);
        }

        public void RemoveAll() => _cache.Clear();
    }
}
