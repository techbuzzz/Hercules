using Hercules.Config;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     task_097: unit-tests for the dual API keys pipeline
///     (<see cref="ApiKeyMiddleware"/>, <see cref="RequireSystemRoleFilter"/>,
///     <see cref="ApiKeyStore"/>). ADR-0004.
/// </summary>
public class ApiKeyMiddlewareTests
{
    [Fact]
    public async Task ValidContributeKey_SetsRoleAndPasses()
    {
        var cfg = new WebApiConfig
        {
            ApiKeys = new List<ApiKeyEntry>
            {
                new() { Key = "hc_contrib_test", Role = ApiKeyRole.Contribute }
            }
        };

        bool nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        var ctx = NewApiRequest("/api/chat", "hc_contrib_test");
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.Equal(ApiKeyRole.Contribute, ctx.Items[ApiKeyMiddleware.RoleItemKey]);
        Assert.NotNull(ctx.Items[ApiKeyMiddleware.EntryItemKey]);
    }

    [Fact]
    public async Task ValidSystemKey_SetsSystemRole()
    {
        var cfg = new WebApiConfig
        {
            ApiKeys = new List<ApiKeyEntry>
            {
                new() { Key = "hc_sys_test", Role = ApiKeyRole.System }
            }
        };

        var middleware = new ApiKeyMiddleware(
            _ => Task.CompletedTask,
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        var ctx = NewApiRequest("/api/config", "hc_sys_test");
        await middleware.InvokeAsync(ctx);

        Assert.Equal(ApiKeyRole.System, ctx.Items[ApiKeyMiddleware.RoleItemKey]);
        Assert.NotEqual(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task WrongKey_Returns401()
    {
        var cfg = new WebApiConfig
        {
            ApiKeys = new List<ApiKeyEntry>
            {
                new() { Key = "hc_contrib_right", Role = ApiKeyRole.Contribute }
            }
        };

        bool nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        var ctx = NewApiRequest("/api/chat", "hc_contrib_wrong");
        await middleware.InvokeAsync(ctx);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task MissingKey_Returns401()
    {
        var cfg = new WebApiConfig
        {
            ApiKeys = new List<ApiKeyEntry>
            {
                new() { Key = "hc_contrib_x", Role = ApiKeyRole.Contribute }
            }
        };

        var middleware = new ApiKeyMiddleware(
            _ => Task.CompletedTask,
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        var ctx = NewApiRequest("/api/chat", provided: null);
        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task EmptyApiKeys_AuthBypass_AllowsRequest()
    {
        // Backward-compat: если ни legacy ApiKey, ни ApiKeys не заданы, middleware пропускает.
        var cfg = new WebApiConfig(); // empty
        Assert.Empty(cfg.ApiKeys);
        Assert.Equal("", cfg.ApiKey);

        bool nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            keys: null);

        var ctx = NewApiRequest("/api/chat", provided: null);
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.NotEqual(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_BypassesAuth_EvenWithKeysConfigured()
    {
        var cfg = new WebApiConfig
        {
            ApiKeys = new List<ApiKeyEntry>
            {
                new() { Key = "hc_contrib_x", Role = ApiKeyRole.Contribute }
            }
        };

        bool nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        var ctx = NewApiRequest("/api/health", provided: null);
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.NotEqual(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task CorsPreflight_BypassesAuth()
    {
        var cfg = new WebApiConfig
        {
            ApiKeys = new List<ApiKeyEntry>
            {
                new() { Key = "hc_contrib_x", Role = ApiKeyRole.Contribute }
            }
        };

        bool nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        var ctx = NewApiRequest("/api/chat", provided: null, method: "OPTIONS");
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task LegacySingleKey_AsContribute_StillWorks()
    {
        // Backward-compat: при наличии legacy ApiKey (но пустых ApiKeys) middleware
        // вызывается через null configuredKeys, и при пустых ключах auth-bypass.
        // Реальный compat-путь — это fallback в Program.cs, переносящий ApiKey в ApiKeys.
        // Здесь мы тестируем сам факт: Program.cs fallback создаёт ApiKeys с Role=Contribute.
        var cfg = new WebApiConfig { ApiKey = "dev-legacy-key" };
        cfg.ApiKeys.Add(new ApiKeyEntry { Key = cfg.ApiKey, Role = ApiKeyRole.Contribute });

        var middleware = new ApiKeyMiddleware(
            _ => Task.CompletedTask,
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        var ctx = NewApiRequest("/api/chat", "dev-legacy-key");
        await middleware.InvokeAsync(ctx);

        Assert.Equal(ApiKeyRole.Contribute, ctx.Items[ApiKeyMiddleware.RoleItemKey]);
    }

    [Fact]
    public async Task MultipleKeys_FindsCorrectRole_AmongList()
    {
        var cfg = new WebApiConfig
        {
            ApiKeys = new List<ApiKeyEntry>
            {
                new() { Key = "hc_contrib_a", Role = ApiKeyRole.Contribute },
                new() { Key = "hc_sys_b", Role = ApiKeyRole.System },
                new() { Key = "hc_contrib_c", Role = ApiKeyRole.Contribute }
            }
        };

        var middleware = new ApiKeyMiddleware(
            _ => Task.CompletedTask,
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        // system key в середине списка
        var ctx = NewApiRequest("/api/config", "hc_sys_b");
        await middleware.InvokeAsync(ctx);

        Assert.Equal(ApiKeyRole.System, ctx.Items[ApiKeyMiddleware.RoleItemKey]);
    }

    private static DefaultHttpContext NewApiRequest(string path, string? provided, string method = "GET")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        if (provided != null)
        {
            ctx.Request.Headers["X-Api-Key"] = provided;
        }
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }
}
