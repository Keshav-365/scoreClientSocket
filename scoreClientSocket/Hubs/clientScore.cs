using BusinessServices.Implementation;
using BusinessServices.Interface;
using Microsoft.AspNetCore.SignalR;
using Modal;
using scoreClientSocket.Services;

namespace scoreClientSocket.Hubs
{
    // Hub is transient — a new instance is created per method call by SignalR.
    // All business logic lives in the getScore singleton so it runs only once.
    // This class is a thin shell: it passes connection-specific context
    // (Context.ConnectionId, Groups, Clients) to the singleton as needed.
    public class clientScore : Hub
    {
        private readonly getScore _gs;
        private readonly IDailyStatsService _dailyStats;
        private readonly IActiveConnectionCountService _connCount;
        private readonly ILogger<clientScore> _logger;


        public clientScore(getScore gs, IDailyStatsService dailyStats, IActiveConnectionCountService connCount, ILogger<clientScore> logger)
        {
            _gs = gs;
            _dailyStats = dailyStats;
            _connCount = connCount;
            _logger = logger;
        }

        #region Lifecycle

        public override async Task OnConnectedAsync()
        {
            try
            {
                // All connections start as client connections.
                // RegisterAsUpstream() will reclassify ScoreProvider connections.
                _ = _dailyStats.IncrClientAsync();
                _ = _connCount.AddConnectionAsync(Context.ConnectionId);
                await base.OnConnectedAsync();
            }
            catch { }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                // Check before removing so we can decrement the right counter.
                bool wasUpstream = await _gs.Upstream.IsUpstreamAsync(Context.ConnectionId);
                await _gs.Upstream.RemoveAsync(Context.ConnectionId);

                if (!wasUpstream)
                    _ = _connCount.RemoveConnectionAsync(Context.ConnectionId);

                await base.OnDisconnectedAsync(exception);
            }
            catch { }
        }

        #endregion

        #region Upstream (ScoreProvider) methods

        public async Task Ping()
            => await Clients.Caller.SendAsync("Pong");

        public async Task RegisterAsUpstream()
        {
            var instanceId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Guid.NewGuid().ToString("N")[..8];

            // Tracker FIRST: if AddAsync throws, this connection is never reclassified,
            // so OnDisconnectedAsync still treats it as a plain client connection.
            try
            {
                await _gs.Upstream.AddAsync(Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Socketlog | RegisterAsUpstream AddAsync failed — upstream NOT registered | ConnId={ConnId} | Instance={Instance} | time={Time}",
                    Context.ConnectionId, instanceId, common.GetDateTime());
                throw;
            }

            _logger.LogWarning("Socketlog | Parent | CONNECTED | ConnectionID={ConnId} | Instance={instanceId} | time={Time}",
                  Context.ConnectionId, instanceId, common.GetDateTime());
            _ = _dailyStats.IncrUpstreamAsync();
            _ = _connCount.RemoveConnectionAsync(Context.ConnectionId); // reclassified from client to upstream
            // Best-effort: both methods have their own internal try/catch.
            await _gs.ReplayPendingRequests(Context.ConnectionId);
            await _gs.SyncCachedEventsToUpstream(Context.ConnectionId);
            var upstreamCount = (await _gs.Upstream.GetAllAsync()).Count;
            _logger.LogWarning("Socketlog | Parent | ConnectionID={ConnId} | NOConnection={NOConnId} | Instance={instanceId} | time={Time}",
                  Context.ConnectionId, upstreamCount.ToString(), instanceId, common.GetDateTime());
        }

        public async Task ReceiveScore(string strEventID, object? scoredata, object? shortscoredata)
            => await _gs.ReceiveAndBroadcast(strEventID, scoredata, shortscoredata);

        public async Task RemoveEvent(string strEventID)
        {
            if (await _gs.Upstream.IsUpstreamAsync(Context.ConnectionId) && int.TryParse(strEventID, out int id))
                _gs.Cache.Remove(id);
        }

        #endregion

        #region End-client methods

        public async Task getscore(string strEventIDs)
        {
            try
            {
                foreach (var strEventID in SplitIds(strEventIDs))
                {
                    if (!int.TryParse(strEventID, out int iEventID) || iEventID <= 0) continue;
                    await Groups.AddToGroupAsync(Context.ConnectionId, strEventID);

                    await _gs.Cache.UpdateConnectionTimeAsync(iEventID);
                    var state = await _gs.Cache.GetAsync(iEventID);
                    if (state?.scoredata != null)
                        await Clients.Caller.SendAsync("Score", state.scoredata);

                    // Always ping upstream so ScoreProvider re-activates polling for this event and
                    // pushes the freshest score — even on a cache hit. Otherwise a client that left
                    // and came back stays stuck on the stale cached score: the parent had let the
                    // event go inactive and, on a hit, nothing told it a client is watching again.
                    await _gs.NotifyUpstream("OnRequestEvent", strEventID);
                }
            }
            catch { }
        }

        public async Task disconnectscore(string strEventIDs)
        {
            try
            {
                foreach (var strEventID in SplitIds(strEventIDs))
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, strEventID);
            }
            catch { }
        }

        public async Task getupdateScore(string strEventIDs)
        {
            try
            {
                foreach (var strEventID in SplitIds(strEventIDs))
                {
                    if (!int.TryParse(strEventID, out int iEventID)) continue;
                    await _gs.Cache.UpdateConnectionTimeAsync(iEventID);
                }
            }
            catch { }
        }

        public async Task getShortScore(string strEventIDs)
        {
            try
            {
                foreach (var strEventID in SplitIds(strEventIDs))
                {
                    if (!int.TryParse(strEventID, out int iEventID) || iEventID <= 0) continue;
                    await Groups.AddToGroupAsync(Context.ConnectionId, common.ShortScoreGroupName + strEventID);

                    await _gs.Cache.UpdateConnectionTimeAsync(iEventID);
                    var state = await _gs.Cache.GetAsync(iEventID);
                    if (state?.shortscoredata != null)
                        await Clients.Caller.SendAsync("ShortScore", state.shortscoredata);

                    // Always ping upstream (see getscore) so a returning client resumes getting
                    // fresh data instead of being stuck on the stale cached short-score.
                    await _gs.NotifyUpstream("OnRequestEvent", strEventID);
                }
            }
            catch { }
        }

        public async Task disconnectShortScore(string strEventIDs)
        {
            try
            {
                foreach (var strEventID in SplitIds(strEventIDs))
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, common.ShortScoreGroupName + strEventID);
            }
            catch { }
        }

        #endregion

        private static IEnumerable<string> SplitIds(string raw) =>
            raw.Trim(',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
