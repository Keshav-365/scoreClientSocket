using BusinessServices.Implementation;
using BusinessServices.Interface;
using Modal;
using scoreprovidersocket.Services;


namespace scoreprovidersocket.code
{
    public static class ContainerExtension
    {
        public static void ConfigureDI(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<IConfiguration>(configuration);

            services.AddSingleton<UpstreamTracker>();
            services.AddSingleton<ILocalEventCacheService, LocalEventCacheService>();
            services.AddSingleton<ICacheSyncService, NullCacheSyncService>();
            services.AddSingleton<IDailyStatsService, NullDailyStatsService>();
            services.AddSingleton<IInstanceTrackerService, NullInstanceTrackerService>();
            services.AddSingleton<IActiveConnectionCountService, LocalActiveConnectionCountService>();

            services.AddSingleton<getScore>();
            services.AddSingleton<IScoreIframeService, ScoreIframeService>();
            services.AddSingleton<IScorecardCacheService, ScorecardCacheService>();
        }
    }
}
