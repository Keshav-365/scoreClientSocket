using BusinessServices.Implementation;
using BusinessServices.Interface;
using Microsoft.AspNetCore.SignalR;
using Modal;
using scoreprovidersocket.Hubs;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace scoreprovidersocket.Services
{
    // Pulls fresh score data for events with recent local client interest straight from
    // ScoreProvider's HTTP API (POST /api/EventInfoBulk), updates the local ConcurrentDictionary
    // event cache (ILocalEventCacheService), and broadcasts changes to subscribed SignalR clients.
    // This is a pull-based complement to the upstream ReceiveScore push (getScore.cs) — it keeps
    // clients moving even if this instance's upstream SignalR connection to ScoreProvider is down.
    public class ScorePollService : BackgroundService
    {
        private readonly ILocalEventCacheService _cache;
        private readonly IHubContext<bfScore> _hub;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ScorePollService> _logger;

        // Per-event tick of the last snapshot broadcast by THIS instance, so an unchanged
        // poll cycle doesn't resend the same data to already-up-to-date clients.
        private readonly ConcurrentDictionary<int, long> _lastBroadcastTicks = new();

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public ScorePollService(ILocalEventCacheService cache, IHubContext<bfScore> hub,
            IHttpClientFactory httpClientFactory, ILogger<ScorePollService> logger)
        {
            _cache = cache;
            _hub = hub;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int interval = AppCache.Settings.ScorePoll.IntervalSeconds;
                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken).ContinueWith(_ => { });
                if (stoppingToken.IsCancellationRequested) break;

                try { await PollAsync(); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ScorePoll | poll cycle failed");
                }
            }
        }

        private async Task PollAsync()
        {
            var settings = AppCache.Settings.ScorePoll;
            string baseUrl = AppCache.Settings.ScoreProvider.BaseUrl;
            if (!settings.Enabled || string.IsNullOrWhiteSpace(baseUrl)) return;

            var cutoff = common.GetDateTime().AddSeconds(-settings.ActiveWindowSeconds);
            var activeIds = _cache.GetAll()
                .Where(e => e.lastconnectiontime >= cutoff)
                .Select(e => e.eventid)
                .ToList();

            if (activeIds.Count == 0) return;

            foreach (var chunk in Chunk(activeIds, settings.MaxEventIdsPerRequest))
                await PollChunkAsync(baseUrl, chunk);
        }

        private async Task PollChunkAsync(string baseUrl, List<int> eventIds)
        {
            BulkResponse? response;
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var body = new { eventIds = string.Join(",", eventIds) };
                using var httpResponse = await client.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/api/EventInfoBulk", body);
                if (!httpResponse.IsSuccessStatusCode) return;

                string strResponms= await httpResponse.Content.ReadAsStringAsync();
                response = await httpResponse.Content.ReadFromJsonAsync<BulkResponse>(JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ScorePoll | EventInfoBulk call failed | count={Count}", eventIds.Count);
                return;
            }

            var states = response?.data?.list;
            if (states == null) return;

            foreach (var state in states)
                await ApplyAsync(state);
        }

        private async Task ApplyAsync(RemoteEventState state)
        {
            await _cache.UpsertAsync(state.eventid, state.scoredata, state.shortscoredata, state.eventstatus ?? "live");

            long ticks = state.lastupdatetime.Ticks;
            long lastSeen = _lastBroadcastTicks.GetValueOrDefault(state.eventid);
            if (ticks <= lastSeen) return;
            _lastBroadcastTicks[state.eventid] = ticks;

            string strEventID = state.eventid.ToString();
            var tasks = new List<Task>(2);

            if (state.scoredata != null)
                tasks.Add(_hub.Clients.Group(strEventID).SendAsync("Score", state.scoredata));
            if (state.shortscoredata != null)
                tasks.Add(_hub.Clients.Group(common.ShortScoreGroupName + strEventID).SendAsync("ShortScore", state.shortscoredata));
            await Task.WhenAll(tasks);
        }

        private static IEnumerable<List<int>> Chunk(List<int> ids, int size)
        {
            for (int i = 0; i < ids.Count; i += size)
                yield return ids.GetRange(i, Math.Min(size, ids.Count - i));
        }

        // Mirrors ScoreProvider's POST /api/EventInfoBulk response shape
        // (ScoreProvider\ScoreProvider\Controllers\APIController.cs — GetEventInfoBulk).
        // Kept minimal on purpose — only the fields this service actually needs.
        private class BulkResponse
        {
            public BulkData? data { get; set; }
        }

        private class BulkData
        {
            public List<RemoteEventState>? list { get; set; }
        }

        private class RemoteEventState
        {
            public int eventid { get; set; }
            public string? eventstatus { get; set; }
            public DateTime lastupdatetime { get; set; }
            public JsonElement? scoredata { get; set; }
            public JsonElement? shortscoredata { get; set; }
        }
    }
}
