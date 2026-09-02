using System.Collections.Generic;

namespace Modal
{
    // Strongly-typed view of appsettings.json. Populated once at startup (Program.cs)
    // and cached on AppCache.Settings for global access.
    public class AppSettings
    {
        public double Version { get; set; }
        public int DevelopmentVersion { get; set; }
        public string ServerName { get; set; }
        public string Port { get; set; }
        public SwaggerSettings SwaggerSettings { get; set; } = new();

        public HubSettings HubSettings { get; set; } = new();
        public CorsSettings Cors { get; set; } = new();
        public ScoreProviderSettings ScoreProvider { get; set; } = new();
        public RateLimitSettings RateLimit { get; set; } = new();
        public ScorecardCacheSettings ScorecardCache { get; set; } = new();
        public UpdateScoreBatchSettings UpdateScoreBatch { get; set; } = new();
        public IdleShutdownSettings IdleShutdown { get; set; } = new();
        public ScorePollSettings ScorePoll { get; set; } = new();
        public AgentAuthSettings AgentAuth { get; set; } = new();
    }

    public class SwaggerSettings
    {
        public bool EnableSwagger { get; set; }
    }


    public class HubSettings
    {
        public string HubName { get; set; }
        public int HubKeepLiveSecond { get; set; }
    }

   
    public class CorsSettings
    {
        public List<string> AllowedOrigins { get; set; } = new();
    }

    public class ScoreProviderSettings
    {
        public string BaseUrl { get; set; } = "";
    }

    public class RateLimitSettings
    {
        public bool isActive { get; set; }
        public int PermitLimit { get; set; } = 1;
        public int WindowSeconds { get; set; } = 10;
    }

    public class ScorecardCacheSettings
    {
        public int LinkTtlMinutes { get; set; } = 60;
    }

    public class UpdateScoreBatchSettings
    {
        public int IntervalSeconds { get; set; } = 20;
    }

    // Controls ScorePollService, which pulls fresh score data for events with recent local
    // client interest from ScoreProvider's HTTP API (POST /api/EventInfoBulk), updates the
    // local ConcurrentDictionary event cache, and broadcasts changes to subscribed clients.
    public class ScorePollSettings
    {
        public bool Enabled { get; set; } = true;
        public int IntervalSeconds { get; set; } = 5;
        // Only events touched (getscore/getupdateScore/getShortScore) within this window are
        // polled — same idea as UpdateScoreBatchService's activity window.
        public int ActiveWindowSeconds { get; set; } = 90;
        // ScoreProvider's EventInfoBulk endpoint caps a single request at this many ids.
        public int MaxEventIdsPerRequest { get; set; } = 30;
    }

    // Forceful (but graceful) self-shutdown of an instance that has served zero
    // end-client connections for a sustained period. See IdleShutdownHostedService.
    public class IdleShutdownSettings
    {
        // Master switch. Disabled by default — opt in per environment.
        public bool Enabled { get; set; } = false;
        // End-client connections must stay at 0 continuously for this long before stopping.
        public int IdleTimeoutSeconds { get; set; } = 300;
        // A freshly-booted instance is not eligible to stop until it has been up this long
        // (it legitimately starts with 0 connections; without this it would exit immediately).
        public int StartupGraceSeconds { get; set; } = 300;
        // How often to evaluate idle state.
        public int CheckIntervalSeconds { get; set; } = 30;
        // Never drain below this many active instances (keeps N warm). 0 allows scaling to zero.
        // The active-instance count always reads as 0 (no cross-instance tracking without
        // Redis), so the instance never self-stops from this check.
        public int MinActiveInstances { get; set; } = 1;
        // Random extra wait (0..N s) before stopping, to de-sync simultaneous shutdowns across replicas.
        public int MaxJitterSeconds { get; set; } = 30;
    }

    // Gates AgentAuthFilter (agent key + per-agent IP whitelist), enforced on every
    // /api/* call and the SignalR hub's negotiate/WebSocket-upgrade requests.
    public class AgentAuthSettings
    {
        public bool isActive { get; set; } = true;
        // Header name callers pass their agent key in. Query string "?key=" is always
        // accepted too, since a browser's native WebSocket upgrade can't carry custom headers.
        public string KeyHeader { get; set; } = "X-App";
    }
}
