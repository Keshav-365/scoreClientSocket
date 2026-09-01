using BusinessServices.Interface;
using Modal;
using Modal.ConcurrentDic;
using System.Collections.Concurrent;

namespace BusinessServices.Implementation
{
    public class LocalEventCacheService : ILocalEventCacheService
    {
        private static readonly ConcurrentDictionary<int, EventState> _cache = new();

        public bool Exists(int eventid) => _cache.ContainsKey(eventid);

        public EventState? Get(int eventid) =>
            _cache.TryGetValue(eventid, out var state) ? state : null;

        public List<EventState> GetAll() => _cache.Values.ToList();

        public EventState Upsert(int eventid, object? scoredata, object? shortscoredata, string eventstatus)
        {
            var state = _cache.GetOrAdd(eventid, id => new EventState { eventid = id });
            lock (state)
            {
                state.scoredata = scoredata ?? state.scoredata;
                state.shortscoredata = shortscoredata ?? state.shortscoredata;
                state.eventstatus = eventstatus;
                state.lastupdatetime = common.GetDateTime();
            }
            return state;
        }

        public Task<EventState?> GetAsync(int eventid) => Task.FromResult(Get(eventid));

        public Task<EventState> UpsertAsync(int eventid, object? scoredata, object? shortscoredata, string eventstatus)
            => Task.FromResult(Upsert(eventid, scoredata, shortscoredata, eventstatus));

        // Create-on-touch: a client subscribing to an event ScoreProvider has never pushed
        // yet must still land a row here (with a fresh lastconnectiontime), otherwise
        // ScorePollService.GetAll() never sees it and can never pull data for it.
        public void UpdateConnectionTime(int eventid)
        {
            var state = _cache.GetOrAdd(eventid, id => new EventState { eventid = id });
            lock (state) { state.lastconnectiontime = common.GetDateTime(); }
        }

        public Task UpdateConnectionTimeAsync(int eventid) { UpdateConnectionTime(eventid); return Task.CompletedTask; }

        public void Remove(int eventid)
        {
            if (eventid <= 0)
                _cache.Clear();
            else
                _cache.TryRemove(eventid, out _);
        }
    }
}
