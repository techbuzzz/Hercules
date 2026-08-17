using System.Globalization;
using Hercules.Config;
using Hercules.Lifecycle;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hercules.WebApi.Middleware;

/// <summary>
///     Rejects new HTTP requests with <c>503 Service Unavailable</c> + <c>Retry-After</c>
///     header as soon as the local agent enters a draining/stopped state. The agent
///     continues to serve the in-flight requests it accepted before draining started
///     (they are tracked separately by <see cref="IInFlightTracker" />).
///     Health and liveness endpoints are exempt so K8s and load balancers can still
///     observe the process.
///     Specification: task_080.
/// </summary>
public sealed class DrainMiddleware
{
    private static readonly string[] ExemptPathPrefixes =
    {
        "/api/health",
        "/api/ready",
        "/api/live",
        $"/{Hercules.BuiltIn.AgentManifestFileName}",
        $"/{Hercules.BuiltIn.AgentCardFileName}"
    };

    private readonly RequestDelegate _next;
    private readonly IAgentLifecycleState _state;
    private readonly ShutdownConfig _shutdown;
    private readonly ILogger<DrainMiddleware> _logger;

    public DrainMiddleware(
        RequestDelegate next,
        IAgentLifecycleState state,
        ShutdownConfig shutdown,
        ILogger<DrainMiddleware> logger)
    {
        _next = next;
        _state = state;
        _shutdown = shutdown;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (_state.IsShuttingDown && !IsExempt(ctx.Request.Path))
        {
            // Retry-After: number of seconds (or HTTP-date). We use a fixed 30s for K8s
            // rolling deploys; the exact value comes from ShutdownConfig.DrainTimeoutSec
            // so operators can tune it.
            var retryAfter = _shutdown.DrainTimeoutSec > 0 ? _shutdown.DrainTimeoutSec : 30;
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ctx.Response.Headers["Retry-After"] = retryAfter.ToString(CultureInfo.InvariantCulture);
            ctx.Response.Headers["Connection"] = "close";
            ctx.Response.ContentType = "application/json";

            _logger.LogInformation(
                "[DrainMiddleware] Rejecting {Method} {Path}: agent is {State}",
                ctx.Request.Method, ctx.Request.Path, _state.State);

            await ctx.Response.WriteAsync(
                $"{{\"error\":\"agent draining\",\"state\":\"{_state.State}\",\"retryAfterSec\":{retryAfter}}}");
            return;
        }

        await _next(ctx);
    }

    private static bool IsExempt(PathString path)
    {
        var s = path.Value;
        if (string.IsNullOrEmpty(s))
        {
            return true;
        }

        foreach (var prefix in ExemptPathPrefixes)
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
