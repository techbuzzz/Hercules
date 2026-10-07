using Hercules.Config;
using Hercules.Lifecycle;
using Hercules.WebApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     task_080: verifies the WebApi <see cref="DrainMiddleware" /> short-circuits with
///     503 + Retry-After when the agent is draining, and forwards the request to the
///     next middleware when it is still running.
/// </summary>
public class DrainMiddlewareTests
{
    [Fact]
    public async Task Returns503_WithRetryAfter_WhenAgentDraining()
    {
        var state = new AgentLifecycleStateHolder();
        state.SetState(AgentLifecycleState.Draining);
        var shutdown = new ShutdownConfig { DrainTimeoutSec = 30 };

        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = new DrainMiddleware(next, state, shutdown, NullLogger<DrainMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/chat";
        ctx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(ctx);

        Assert.False(called, "next middleware should NOT be called when draining");
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
        Assert.Equal("30", ctx.Response.Headers["Retry-After"]);
    }

    [Fact]
    public async Task Returns503_WithRetryAfterZero_WhenDrainTimeoutIsZero()
    {
        var state = new AgentLifecycleStateHolder();
        state.SetState(AgentLifecycleState.Draining);
        var shutdown = new ShutdownConfig { DrainTimeoutSec = 0 };

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new DrainMiddleware(next, state, shutdown, NullLogger<DrainMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/chat";
        ctx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
        // 0 is falsy in the middleware — falls back to 30s so K8s always has a positive delay.
        Assert.Equal("30", ctx.Response.Headers["Retry-After"]);
    }

    [Fact]
    public async Task ExemptPaths_BypassDrainCheck_EvenWhenDraining()
    {
        var state = new AgentLifecycleStateHolder();
        state.SetState(AgentLifecycleState.Draining);
        var shutdown = new ShutdownConfig { DrainTimeoutSec = 30 };

        var middleware = new DrainMiddleware(
            _ => Task.CompletedTask,
            state,
            shutdown,
            NullLogger<DrainMiddleware>.Instance);

        foreach (var path in new[] { "/api/health", "/api/ready", "/api/health/detail", "/agent.manifest.json" })
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Path = path;
            ctx.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(ctx);

            Assert.NotEqual(
                StatusCodes.Status503ServiceUnavailable,
                ctx.Response.StatusCode);
        }
    }

    [Fact]
    public async Task PassesThrough_WhenAgentRunning()
    {
        var state = new AgentLifecycleStateHolder();
        Assert.Equal(AgentLifecycleState.Running, state.State);
        var shutdown = new ShutdownConfig();

        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = new DrainMiddleware(next, state, shutdown, NullLogger<DrainMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/chat";
        ctx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(ctx);

        Assert.True(called);
        Assert.NotEqual(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
    }
}
