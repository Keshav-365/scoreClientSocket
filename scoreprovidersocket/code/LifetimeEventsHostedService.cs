using Microsoft.AspNetCore.Mvc;

namespace scoreprovidersocket.code
{
    internal class LifetimeEventsHostedService : IHostedService
    {
        private readonly IHostApplicationLifetime _appLifetime;

        // 2. Inject `IHostApplicationLifetime` through dependency injection in the constructor.
        public LifetimeEventsHostedService(IHostApplicationLifetime appLifetime)
        {
            _appLifetime = appLifetime;
        }

        // 3. Implemented by `IHostedService`, setup here your event registration. 
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStopping.Register(OnStopping);

            return Task.CompletedTask;
        }

        // 4. Implemented by `IHostedService`, setup here your shutdown registration.
        //    If you have nothing to stop, then just return `Task.CompletedTask`
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private async void OnStarted()
        {

            // Perform post-startup activities here
        }

        private async void OnStopping()
        {
            // Perform on-stopping activities here
        }

        private void OnStopped()
        {

            // Perform post-stopped activities here
        }
    }
}
