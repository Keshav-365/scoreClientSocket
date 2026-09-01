using BusinessServices.Implementation;
using BusinessServices.Interface;
using Microsoft.AspNetCore.SignalR;
using Modal;
using scoreClientSocket.Hubs;
using System.Collections.Concurrent;

namespace scoreClientSocket.Services
{
    // Singleton — created once at startup. Holds all score logic so the transient
    // clientScore hub only needs one constructor parameter (this class).
    public class getScore
    {
        private readonly ILocalEventCacheService _cache;
        private readonly IHubContext<clientScore> _hub;
        private readonly UpstreamTracker _upstream;

        // Tracks event IDs whose OnRequestEvent was fired but no ReceiveScore has arrived yet.
        // Replayed to any newly registered upstream to cover the reconnect-window gap.
        private readonly ConcurrentDictionary<string, byte> _pendingEventRequests = new();

        public getScore(ILocalEventCacheService cache, IHubContext<clientScore> hub, UpstreamTracker upstream)
        {
            _cache = cache;
            _hub = hub;
            _upstream = upstream;
        }

        public ILocalEventCacheService Cache => _cache;
        public UpstreamTracker Upstream => _upstream;

        #region Upstream (ScoreProvider) callbacks

        // Cache update and both group broadcasts run concurrently so clients receive
        // data as fast as possible.
        public async Task ReceiveAndBroadcast(string strEventID, object? scoredata, object? shortscoredata)
        {
            try
            {
                if (string.IsNullOrEmpty(strEventID)) return;

                _pendingEventRequests.TryRemove(strEventID, out _);

                var tasks = new List<Task>(3);

                if (int.TryParse(strEventID, out int iEventID))
                    tasks.Add(_cache.UpsertAsync(iEventID, scoredata, shortscoredata, "live"));

                if (scoredata != null)
                    tasks.Add(_hub.Clients.Group(strEventID).SendAsync("Score", scoredata));

                if (shortscoredata != null)
                    tasks.Add(_hub.Clients.Group(common.ShortScoreGroupName + strEventID)
                                  .SendAsync("ShortScore", shortscoredata));

                await Task.WhenAll(tasks);
            }
            catch { }
        }

        #endregion

        #region Upstream notification helpers

        // Fan out to all registered upstream connections in parallel.
        // Stale connection IDs (leftover after ungraceful disconnects) are
        // removed lazily when their send fails.
        public async Task NotifyUpstream(string method, string eventId)
        {
            var ids = await _upstream.GetAllAsync();
            if (ids.Count == 0) return;
            if (method == "OnRequestEvent")
            {
                // Track regardless of whether upstream is connected right now.
                // ReplayPendingRequests() will replay when upstream reconnects.
                _pendingEventRequests.TryAdd(eventId, 0);
            }
            await Task.WhenAll(ids.Select(async id =>
            {
                try
                {
                    await _hub.Clients.Client(id).SendAsync(method, eventId);
                }
                catch
                {
                    await _upstream.RemoveAsync(id);
                }
            }));
        }

        // Called by clientScore.RegisterAsUpstream() so that any OnRequestEvent calls that were
        // fired during a reconnect window (no upstream registered) are replayed to the newly
        // connected ScoreProvider instance.
        public async Task ReplayPendingRequests(string upstreamConnectionId)
        {
            var pending = _pendingEventRequests.Keys.ToList();
            if (pending.Count == 0) return;

            foreach (var eventId in pending)
            {
                try
                {
                    await _hub.Clients.Client(upstreamConnectionId).SendAsync("OnRequestEvent", eventId);
                }
                catch { }
            }
        }

        // Called by clientScore.RegisterAsUpstream() to tell the newly connected ScoreProvider
        // which event IDs are already in our cache. ScoreProvider will push fresh data for
        // any it has, and seed any it doesn't know about yet.
        public async Task SyncCachedEventsToUpstream(string upstreamConnectionId)
        {
            var events = _cache.GetAll();
            if (events.Count == 0) return;

            var ids = string.Join(",", events.Select(e => e.eventid.ToString()));
            try
            {
                await _hub.Clients.Client(upstreamConnectionId).SendAsync("OnSyncCachedEvents", ids);
            }
            catch { }
        }

        #endregion
    }
}
