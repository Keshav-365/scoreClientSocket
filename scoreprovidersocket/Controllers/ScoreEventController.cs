using BusinessServices.Implementation;
using BusinessServices.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Modal;

namespace scoreprovidersocket.Controllers
{
    [Route("api")]
    [ApiController]
    [EnableRateLimiting("api-ip-limit")]
    public class ScoreEventController : ControllerBase
    {
        private readonly ILocalEventCacheService _cache;
        private readonly IScoreIframeService _scoreIframe;
        private readonly ICacheSyncService _cacheSync;
        private readonly UpstreamTracker _upstream;
        private readonly IScorecardCacheService _scorecardCache;
        private readonly IDailyStatsService _dailyStats;
        private readonly IInstanceTrackerService _instanceTracker;
        private readonly IActiveConnectionCountService _connCount;
        private readonly IHttpClientFactory _httpClientFactory;

        public ScoreEventController(ILocalEventCacheService cache, IScoreIframeService scoreIframe, ICacheSyncService cacheSync, UpstreamTracker upstream, IScorecardCacheService scorecardCache, IDailyStatsService dailyStats, IInstanceTrackerService instanceTracker, IActiveConnectionCountService connCount, IHttpClientFactory httpClientFactory)
        {
            _cache = cache;
            _scoreIframe = scoreIframe;
            _cacheSync = cacheSync;
            _upstream = upstream;
            _scorecardCache = scorecardCache;
            _dailyStats = dailyStats;
            _instanceTracker = instanceTracker;
            _connCount = connCount;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        [Route("ip")]
        public async Task<object> API()
        {
            string ip;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                try
                {
                    ip = (await client.GetStringAsync("https://api.ipify.org")).Trim();
                }
                catch (Exception ex)
                {
                    ip = $"unavailable ({ex.GetType().Name}: {ex.Message})";
                }
            }

            var vresposne = new
            {
                outbound_ip = ip,
                timestamp = common.GetDateTime(),
                utc_time = DateTime.UtcNow,
                ist_time = common.GetDateTime(),
                timezone = TimeZoneInfo.Local.DisplayName
            };

            return new
            {
                issuccess = true,
                data = vresposne,
                message = "successfully call",
                statuscode = StatusCodes.Status200OK
            };
        }

        // GET /api/Scorecard?eventId=123&link=0&pid=0&color=&font=
        // 1. Check local cache by (eventId, link, pid)
        // 2. On miss: call ScoreProvider api/Scorecard, cache the base URL, then return
        // Color and font are appended after cache lookup so the cached URL stays clean.
        [HttpGet]
        [Route("Scorecard")]
        [Route("bfrateScoreborad")]
        public async Task<ScoreResponse> GetScorecard(string eventId, int link = 0, string color = "", string font = "", int pid = 0)
        {
            if (string.IsNullOrEmpty(eventId)) return new ScoreResponse();

            try
            {
                var cached = _scorecardCache.Get(eventId, link, pid);
                if (cached == null || string.IsNullOrEmpty(cached.ScoreUrl))
                {
                    var fromProvider = await _scoreIframe.GetScorecardAsync(eventId, link, pid);
                    if (fromProvider != null)
                    {
                        _scorecardCache.Set(eventId, link, pid, fromProvider);
                        cached = fromProvider;
                    }
                }

                if (cached == null) return new ScoreResponse();

                // Return a copy with color/font appended so the cached entry stays decoration-free.
                var result = new ScoreResponse { EventID = cached.EventID, ScoreUrl = cached.ScoreUrl, StreamingUrl = cached.StreamingUrl };
                if (!string.IsNullOrEmpty(result.ScoreUrl))
                {
                    if (!string.IsNullOrEmpty(color)) result.ScoreUrl += "&color=" + color;
                    if (!string.IsNullOrEmpty(font)) result.ScoreUrl += "&font=" + font;
                }
                return result;
            }
            catch
            {
                return new ScoreResponse();
            }
        }

        [HttpGet("ScoreIframe")]
        public async Task<clsResponse> GetScoreIframe(string eventId, int link = 0, string color = "", string font = "")
        {
            clsResponse _ResultRespose = new clsResponse();
            _ResultRespose.success = false;
            _ResultRespose.status = StatusCodes.Status200OK;

            if (string.IsNullOrEmpty(eventId))
            {
                _ResultRespose.message = "Required parameter missing !!";
            }
            else
            {
                var result = await _scoreIframe.GetAsync(eventId, link, color, font);
                if (result == null)
                {
                    _ResultRespose.message = "ScoreProvider unavailable or BaseUrl not configured.";
                }
                else
                {
                    _ResultRespose = result;
                }
            }
            return _ResultRespose;
        }
        // GET /api/ConnectionCount
        // this_instance — live counts for the instance that answered this request.
        // global        — same as this_instance; there is no cross-instance aggregation
        //                 without a shared store.
        [HttpGet("ConnectionCount")]
        public async Task<IActionResult> ConnectionCount()
        {
            var localClients = await _connCount.GetLocalAsync();
            var upstream = (await _upstream.GetAllAsync()).Count;
            var globalClients = await _connCount.GetGlobalAsync();

            return Ok(new
            {
                issuccess = true,
                data = new
                {
                    this_instance = new
                    {
                        client_connections  = localClients,
                        upstream_connections = upstream,
                        total_connections   = localClients + upstream
                    },
                    global = new
                    {
                        client_connections = globalClients
                    }
                }
            });
        }


        // GET /api/ConnectionStats — active connections on this instance + daily totals (IST).
        // active.*  = live counts for THIS instance only.
        // today/yesterday = daily counters; always zero without a shared store (NullDailyStatsService).
        [HttpGet("ConnectionStats")]
        public async Task<IActionResult> ConnectionStats()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
            var todayStats = await _dailyStats.GetAsync(today);
            var yesterdayStats = await _dailyStats.GetAsync(today.AddDays(-1));

            var clients = await _connCount.GetLocalAsync();
            var upstream = (await _upstream.GetAllAsync()).Count;

            return Ok(new
            {
                issuccess = true,
                data = new
                {
                    active = new
                    {
                        client_connections = clients,
                        upstream_connections = upstream,
                        total = clients + upstream
                    },
                    today = new
                    {
                        date = todayStats.Date,
                        new_connections = todayStats.Clients,
                        new_upstream = todayStats.Upstream
                    },
                    yesterday = new
                    {
                        date = yesterdayStats.Date,
                        new_connections = yesterdayStats.Clients,
                        new_upstream = yesterdayStats.Upstream
                    }
                }
            });
        }

        // GET /api/InstanceStats — Cloud Run instance lifecycle data.
        // active.instances lists every instance currently sending heartbeats (last 120 s).
        // history lists recent instances (active + destroyed) with created/destroyed times.
        // total_started = all-time lifetime counter.
        // today/yesterday = instances started on that IST date.
        // Always empty/zero without a shared store (NullInstanceTrackerService).
        [HttpGet("InstanceStats")]
        public async Task<IActionResult> InstanceStats()
        {
            var stats = await _instanceTracker.GetStatsAsync();

            var instanceDetails = await Task.WhenAll(stats.ActiveInstances.Select(async i =>
            {
                var connCount = await _connCount.GetLocalAsync();
                return new
                {
                    id                 = i.Id,
                    started_at         = i.StartedAt,
                    is_this_instance   = i.Id == _connCount.InstanceId,
                    client_connections = i.Id == _connCount.InstanceId ? connCount : 0L
                };
            }));

            // Lifecycle history: destroyed_at is null while the instance is still running.
            var history = stats.RecentInstances.Select(h => new
            {
                id             = h.Id,
                started_at     = h.StartedAt,
                destroyed_at   = h.DestroyedAt,
                destroy_reason = h.DestroyReason,
                is_active      = h.DestroyedAt == null
            });

            return Ok(new
            {
                issuccess = true,
                data = new
                {
                    active = new
                    {
                        count     = stats.ActiveCount,
                        instances = instanceDetails
                    },
                    history,
                    total_started = stats.TotalStarted,
                    today = new
                    {
                        date    = stats.TodayDate,
                        started = stats.TodayStarted
                    },
                    yesterday = new
                    {
                        date    = stats.YesterdayDate,
                        started = stats.YesterdayStarted
                    }
                }
            });
        }

        // GET /api/EventList — all events currently in local CCD
        [HttpGet("EventList")]
        public IActionResult GetEventList()
        {
            var list = _cache.GetAll()
               .OrderByDescending(e => e.lastupdatetime)
               .ToList();
            return Ok(new { issuccess = true, count = list.Count, data = list });
        }

        // GET /api/EventInfo?eventId=12345 — single event detail from local CCD
        [HttpGet("EventInfo")]
        public IActionResult GetEventInfo(int eventId)
        {
            var state = _cache.Get(eventId);
            if (state == null)
                return NotFound(new { issuccess = false, message = $"Event {eventId} not found in cache." });

            return Ok(new { issuccess = true, data = Newtonsoft.Json.JsonConvert.SerializeObject(state) });
        }

        // GET /api/ClearEvent?eventId=12345 — remove specific event from local CCD
        // GET /api/ClearEvent?eventId=0     — clear ALL events from the local cache
        //                                     and broadcast to every load-balanced instance
        [HttpGet("ClearEvent")]
        public async Task<IActionResult> ClearEvent(int eventId = 0)
        {
            _cache.Remove(eventId);
            if (eventId <= 0)
                await _cacheSync.PublishClearAllAsync();
            string msg = eventId <= 0 ? "All events cleared on all instances." : $"Event {eventId} cleared.";
            return Ok(new { issuccess = true, message = msg });
        }

        // GET /api/ClearlinkCache?eventId=123 — remove scorecard link cache for that event
        // GET /api/ClearlinkCache?eventId=0   — remove ALL scorecard link cache entries
        [HttpGet("ClearlinkCache")]
        public IActionResult ClearScorecardCache(string eventId = "0")
        {
            if (eventId == "0")
            {
                _scorecardCache.RemoveAll();
                return Ok(new { issuccess = true, message = "All scorecard link cache cleared." });
            }
            _scorecardCache.Remove(eventId);
            return Ok(new { issuccess = true, message = $"Scorecard link cache cleared for event {eventId}." });
        }

        // GET /api/CheckWidget?url=... — server-side HEAD request to check if a widget URL is reachable.
        // Returns { available: true } on HTTP 2xx, { available: false } on 4xx/5xx or network error.
        // Used by score-webapp to avoid showing a broken iframe when Betfair has no stream for an event.
        [HttpGet("CheckWidget")]
        public async Task<IActionResult> CheckWidget(string url)
        {
            if (string.IsNullOrEmpty(url))
                return Ok(new { available = false });
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                return Ok(new { available = response.IsSuccessStatusCode });
            }
            catch
            {
                return Ok(new { available = false });
            }
        }

        // GET /api/widget-proxy?url=...
        // Server-side reverse proxy that strips X-Frame-Options / Content-Security-Policy so
        // the Betfair videoplayer can be embedded in an iframe from any origin.
        // Whitelisted to https://videoplayer.betfair.com/ only to prevent SSRF abuse.
        [HttpGet("widget-proxy")]
        public async Task<IActionResult> WidgetProxy([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url) ||
                !url.StartsWith("https://videoplayer.betfair.com/", StringComparison.OrdinalIgnoreCase))
                return BadRequest("URL not allowed");

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var betfairResponse = await client.SendAsync(request);
                var contentType = betfairResponse.Content.Headers.ContentType?.ToString()
                                  ?? "text/html; charset=utf-8";

                // ReadAsStringAsync handles Content-Encoding decompression automatically.
                // Deliberately NOT forwarding X-Frame-Options or Content-Security-Policy
                // so the browser allows this page to render inside our iframe.
                var html = await betfairResponse.Content.ReadAsStringAsync();
                return Content(html, contentType);
            }
            catch
            {
                return StatusCode(502, "Widget upstream unavailable");
            }
        }

    }
}
