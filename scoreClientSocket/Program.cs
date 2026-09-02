using BusinessServices.Implementation;
using BusinessServices.Interface;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;
using Modal;
using Newtonsoft.Json;
using scoreClientSocket.code;
using scoreClientSocket.Hubs;
using scoreClientSocket.Services;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

string strProject = "ScoreClientSocket";
var builder = WebApplication.CreateBuilder(args);
// Add services to the container.
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
    });

builder.Services
           .ConfigureHttpJsonOptions(options =>
           {
               var serializerOptions = options.SerializerOptions;

               serializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault | JsonIgnoreCondition.WhenWritingNull;
               serializerOptions.Converters.Add(new JsonStringEnumConverter());
           }
               )
           .Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
           {
               options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
           }
               )
           .AddResponseCompression(options =>
           {
               var providers = options.Providers;

               providers.Add<BrotliCompressionProvider>();
               providers.Add<GzipCompressionProvider>();

               options.EnableForHttps = true;
           }
               )
           .Configure<BrotliCompressionProviderOptions>(options => { options.Level = CompressionLevel.Fastest; })
           .Configure<GzipCompressionProviderOptions>(options => { options.Level = CompressionLevel.Fastest; })
           .AddEndpointsApiExplorer();

// Load configuration
var configuration = builder.Configuration;

// Bind the strongly-typed application settings and cache them for global access.
var appSettings = new AppSettings();
configuration.Bind(appSettings);
// Sections whose JSON keys don't match a C# property name are bound explicitly.
configuration.GetSection("Hubettings").Bind(appSettings.HubSettings);
// Env vars take precedence over appsettings.json — see ApplyEnvOverrides at bottom of file.
ApplyEnvOverrides(appSettings);
AppCache.Settings = appSettings;

// Agent keys + per-agent IP whitelists, checked by AgentAuthFilter.
var agents = configuration.GetSection("Agents").Get<List<Modal.Cache.AgentConfig>>();
AppCache.Agents = agents ?? new List<Modal.Cache.AgentConfig>();


bool enableSwagger = appSettings.SwaggerSettings.EnableSwagger;//Convert.ToBoolean(configuration["SwaggerSettings:EnableSwagger"].ToString());
string developmentVersion = "v" + AppCache.Settings.DevelopmentVersion;// configuration["DevelopmentVersion"].ToString();

if (enableSwagger)
{
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc(developmentVersion, new OpenApiInfo { Title = strProject, Version = developmentVersion });

        // When AgentAuthFilter is enforcing keys, give Swagger UI an "Authorize" button so the
        // key gets attached to every "Try it out" call instead of failing with 401.
        if (AppCache.Settings.AgentAuth.isActive)
        {
            string keyHeader = string.IsNullOrEmpty(AppCache.Settings.AgentAuth.KeyHeader)
                ? "X-App"
                : AppCache.Settings.AgentAuth.KeyHeader;

            c.AddSecurityDefinition("agentKey", new OpenApiSecurityScheme
            {
                Name = keyHeader,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "Agent key required by AgentAuthFilter (see appsettings.json Agents)."
            });

            var securityRequirement = new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "agentKey" }
                    },
                    new string[] { }
                }
            };
            c.AddSecurityRequirement(securityRequirement);
        }
    });
}

if (!string.IsNullOrEmpty(AppCache.Settings.Port))
{
    // Bind on all interfaces (was "localhost") so the app is reachable inside containers.
    // Command-line --urls and ASPNETCORE_URLS still override this when set.
    builder.WebHost.UseUrls($"http://+:{AppCache.Settings.Port}");
}

builder.Services.AddHttpClient();

// IP-based rate limit: 1 request per 10 seconds per client IP on all API endpoints.
// Enabled only when RateLimit:isActive = true in appsettings.
if (AppCache.Settings.RateLimit.isActive)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("api-ip-limit", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                              ?? ctx.Connection.RemoteIpAddress?.ToString()
                              ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = AppCache.Settings.RateLimit.PermitLimit,
                    Window = TimeSpan.FromSeconds(AppCache.Settings.RateLimit.WindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });
}

// Add services to the container.
builder.Services.AddOptions();

int iKeepLiveSeconds = AppCache.Settings.HubSettings.HubKeepLiveSecond;// Convert.ToInt32(configuration["Hubettings:HubKeepLiveSecond"].ToString());
builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.EnableDetailedErrors = true;
    // Server pings every 20 s — keeps the TCP stream active through GCP's load balancer.
    hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(iKeepLiveSeconds);
    // Disconnect a client only after 90 s of silence (≈ 3 missed pings).
    // Default is 2× KeepAliveInterval = 40 s, which is too tight for GCP's transient delays.
    hubOptions.ClientTimeoutInterval = TimeSpan.FromSeconds(90);
    hubOptions.MaximumReceiveMessageSize = null;
});

// Add services to the container.
builder.Services.ConfigureDI(configuration);
builder.Services.AddHostedService<UpdateScoreBatchService>();
builder.Services.AddHostedService<InstanceTrackerHostedService>();
builder.Services.AddHostedService<IdleShutdownHostedService>();
builder.Services.AddHostedService<ScorePollService>();
List<string> allowedOrigins = AppCache.Settings.Cors.AllowedOrigins;//  builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins != null && allowedOrigins.Count > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("CorsPolicy", corsBuilder =>
        {
            corsBuilder
                .WithOrigins(allowedOrigins.ToArray())   // read from config
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
    });

}
else
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("CorsPolicy", corsBuilder =>
        {
            corsBuilder
                .SetIsOriginAllowed(origin => true)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
    });
}

var app = builder.Build();

// HOSTNAME is set automatically by Cloud Run to a value unique per container instance.
// Falls back to a short GUID when running locally.
var instanceId = AppCache.InstanceId;

// Use an explicit named logger (not app.Logger which is ILogger<WebApplication> with category
// "Microsoft.AspNetCore.Builder.WebApplication" — that category gets filtered as a framework
// log in Cloud Run's log viewer). "ScoreSocket" falls under Default=Information, always visible.
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ScoreSocket");

app.Lifetime.ApplicationStarted.Register(() =>
    startupLogger.LogWarning("ScoreClientSocket | Instance | STARTED | instance={InstanceId} | hub={Hub} | time={Time}",
        instanceId, AppCache.Settings.HubSettings.HubName, common.GetDateTime()));

app.Lifetime.ApplicationStopping.Register(() =>
{
    // Write directly to stderr FIRST — synchronous flush, survives Cloud Run's SIGKILL window.
    // The Console logger uses a background queue that may not drain before SIGKILL arrives.
    Console.Error.WriteLine(
        $"ScoreClientSocket | Instance | STOPPING | instance={instanceId} | hub={AppCache.Settings.HubSettings.HubName} | time={common.GetDateTime()}");
    startupLogger.LogWarning("ScoreClientSocket | Instance | STOPPING | instance={InstanceId} | hub={Hub} | time={Time}",
        instanceId, AppCache.Settings.HubSettings.HubName, common.GetDateTime());
});

app.UseRouting();
if (AppCache.Settings.RateLimit.isActive)
    app.UseRateLimiter();
app.UseCors("CorsPolicy");

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint($"/swagger/{developmentVersion}/swagger.json", $"{strProject} {developmentVersion}");
        c.RoutePrefix = "swagger";
    });
}

app.UseCookiePolicy();
app.UseDefaultFiles();
app.UseStaticFiles();

// UseDefaultFiles only resolves a directory's index.html when the URL ends in "/" —
// "/docs" (no trailing slash) doesn't match any static file and falls through to
// AgentAuthFilter below, which then wrongly demands a key for what should be a public
// docs page. Redirect the bare path to the trailing-slash form before that happens.
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/docs")
    {
        context.Response.Redirect($"{context.Request.PathBase}/docs/", permanent: true);
        return;
    }
    await next();
});

// Agent key + per-agent IP whitelist check. Runs after static files (wwwroot stays public)
// and before endpoint routing, so it covers both /api/* controller calls and the SignalR
// hub's negotiate/WebSocket-upgrade requests.
app.UseAgentAuthFilter();

// Configure the HTTP request pipeline.
app.UseEndpoints(endpoints =>
{
    endpoints.MapDefaultControllerRoute();
    endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
    if (!string.IsNullOrEmpty(AppCache.Settings.HubSettings.HubName))
    {
        endpoints.MapHub<clientScore>($"/{AppCache.Settings.HubSettings.HubName}", options =>
        {
            options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
        });
    }
});

if (!string.IsNullOrEmpty(AppCache.Settings.HubSettings.HubName))
{
    var _GetScore = app.Services.GetRequiredService<getScore>();
}
app.Run();

// ── Env var overrides ────────────────────────────────────────────────────────
// Called right after configuration.Bind() so that every env var is in effect
// before any code reads AppCache.Settings.
// Naming convention: SCREAMING_SNAKE_CASE.
// bool/int: only applied when the env var is present and parseable.
// List (CORS_ALLOWED_ORIGINS, etc.): comma-separated.
static void ApplyEnvOverrides(AppSettings s)
{
    Func<string, string?> e = Environment.GetEnvironmentVariable;

    if (int.TryParse(e("DEVELOPMENT_VERSION"), out var dv)) s.DevelopmentVersion = dv;
    s.ServerName = e("SERVER_NAME") ?? s.ServerName;
    s.Port = e("PORT") ?? s.Port;
    if (bool.TryParse(e("SWAGGER_ENABLED"), out var sw)) s.SwaggerSettings.EnableSwagger = sw;
    s.HubSettings.HubName = e("HUB_NAME") ?? s.HubSettings.HubName;
    if (int.TryParse(e("HUB_KEEPALIVE_SECONDS"), out var kl)) s.HubSettings.HubKeepLiveSecond = kl;
    var origins = e("CORS_ALLOWED_ORIGINS");
    if (!string.IsNullOrEmpty(origins))
        s.Cors.AllowedOrigins = origins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    if (bool.TryParse(e("RATE_LIMIT_ACTIVE"), out var rl)) s.RateLimit.isActive = rl;
    if (int.TryParse(e("RATE_LIMIT_PERMIT"), out var rp)) s.RateLimit.PermitLimit = rp;
    if (int.TryParse(e("RATE_LIMIT_WINDOW"), out var rw)) s.RateLimit.WindowSeconds = rw;

    s.ScoreProvider.BaseUrl = e("SCORE_PROVIDER_BASEURL") ?? s.ScoreProvider.BaseUrl;
    if (int.TryParse(e("SCORECARDCACHE_LINKTTLMINUTES"), out var slm)) s.ScorecardCache.LinkTtlMinutes = slm;
    if (int.TryParse(e("UPDATE_SCOREBATCH_INTERVAL"), out var ubi)) s.UpdateScoreBatch.IntervalSeconds = ubi;

    if (bool.TryParse(e("IDLE_SHUTDOWN_ENABLED"), out var ise)) s.IdleShutdown.Enabled = ise;
    if (int.TryParse(e("IDLE_SHUTDOWN_TIMEOUT"), out var ist)) s.IdleShutdown.IdleTimeoutSeconds = ist;
    if (int.TryParse(e("IDLE_SHUTDOWN_GRACE"), out var isg)) s.IdleShutdown.StartupGraceSeconds = isg;
    if (int.TryParse(e("IDLE_SHUTDOWN_MIN_INSTANCES"), out var ism)) s.IdleShutdown.MinActiveInstances = ism;

    if (bool.TryParse(e("SCOREPOLL_ENABLED"), out var spe)) s.ScorePoll.Enabled = spe;
    if (int.TryParse(e("SCOREPOLL_INTERVAL_SECONDS"), out var spi)) s.ScorePoll.IntervalSeconds = spi;
    if (int.TryParse(e("SCOREPOLL_ACTIVEWINDOW_SECONDS"), out var spw)) s.ScorePoll.ActiveWindowSeconds = spw;

    if (bool.TryParse(e("AGENT_AUTH_ACTIVE"), out var aaa)) s.AgentAuth.isActive = aaa;
    s.AgentAuth.KeyHeader = e("AGENT_AUTH_KEY_HEADER") ?? s.AgentAuth.KeyHeader;
}