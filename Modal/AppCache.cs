using System;
using System.Collections.Generic;
using System.Text;

namespace Modal
{
    public static class AppCache
    {
        public static AppSettings Settings { get; set; } = new();

        // Computed once at first access — guaranteed same value across all singletons.
        // Uses HOSTNAME (set automatically by Cloud Run per container) or falls back
        // to a short random GUID for local development.
        public static string InstanceId { get; } =
            Environment.GetEnvironmentVariable("HOSTNAME")
            ?? Guid.NewGuid().ToString("N")[..8];

        // Set just before a self-initiated stop (e.g. IdleShutdownHostedService) so the
        // instance lifecycle history can record WHY it was destroyed. Null = ordinary
        // graceful stop (deploy, scale-in, SIGTERM).
        public static volatile string? ShutdownReason;
    }
}
