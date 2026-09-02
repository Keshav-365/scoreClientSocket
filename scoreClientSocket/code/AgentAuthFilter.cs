using Modal;
using Newtonsoft.Json;
using System.Text;

namespace scoreClientSocket.code
{
    // Gates every /api/* call and the SignalR hub's negotiate + WebSocket-upgrade requests
    // (both flow through this middleware before reaching UseEndpoints/MapHub). Callers must
    // present a non-expired agent key — via the configured header (default "X-App") or a
    // "?key=" query string, since a browser's native WebSocket upgrade can't carry custom
    // headers — and, if that agent's AllowedIPs is non-empty, connect from a listed IP.
    // Swagger and static files (wwwroot) are left open.
    public class AgentAuthFilter
    {
        private readonly RequestDelegate _next;
        private readonly bool _isActive;
        private readonly string _keyHeader;

        public AgentAuthFilter(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _isActive = AppCache.Settings.AgentAuth.isActive;
            _keyHeader = string.IsNullOrEmpty(AppCache.Settings.AgentAuth.KeyHeader)
                ? "X-App"
                : AppCache.Settings.AgentAuth.KeyHeader;
        }

        public async Task Invoke(HttpContext context)
        {
            string path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
            if (!_isActive || path.Contains("swagger"))
            {
                await _next(context);
                return;
            }

            string key = context.Request.Headers.ContainsKey(_keyHeader)
                ? context.Request.Headers[_keyHeader].ToString()
                : context.Request.Query["key"].ToString();

            if (string.IsNullOrEmpty(key))
            {
                await Reject(context, StatusCodes.Status401Unauthorized, "Agent key required.");
                return;
            }

            var agent = AppCache.Agents.FirstOrDefault(a => a.Key == key);
            if (agent == null)
            {
                await Reject(context, StatusCodes.Status401Unauthorized, "Invalid agent key.");
                return;
            }

            if (!DateTime.TryParse(agent.ExpiryDate, out DateTime expiryDate))
            {
                await Reject(context, StatusCodes.Status401Unauthorized, "Invalid expiry date configured for agent.");
                return;
            }

            if (DateTime.UtcNow.Date > expiryDate.Date)
            {
                await Reject(context, StatusCodes.Status401Unauthorized, $"Agent '{agent.Name}' key has expired on {agent.ExpiryDate}.");
                return;
            }

            if (agent.AllowedIPs != null && agent.AllowedIPs.Count > 0)
            {
                string ip = ResolveClientIp(context);
                if (!agent.AllowedIPs.Contains(ip))
                {
                    await Reject(context, StatusCodes.Status403Forbidden, $"IP '{ip}' is not whitelisted for agent '{agent.Name}'.");
                    return;
                }
            }

            context.Items["Agent"] = agent;
            await _next(context);
        }

        private static string ResolveClientIp(HttpContext context)
        {
            if (context.Request.Headers.ContainsKey("X-Forwarded-For"))
            {
                string forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
                if (!string.IsNullOrEmpty(forwarded))
                    return forwarded.Split(',')[0].Trim();
            }
            if (context.Request.Headers.ContainsKey("CF-CONNECTING-IP"))
                return context.Request.Headers["CF-CONNECTING-IP"].ToString();

            return context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";
        }

        private static async Task Reject(HttpContext context, int statusCode, string message)
        {
            var response = new clsResponse { success = false, message = message, status = statusCode };
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonConvert.SerializeObject(response), Encoding.UTF8);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class AgentAuthFilterExtensions
    {
        public static IApplicationBuilder UseAgentAuthFilter(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AgentAuthFilter>();
        }
    }
}
