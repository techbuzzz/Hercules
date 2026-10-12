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

    // ---------------------------------------------------------------------
    // R5: the documented legacy WebApi:ApiKey fallback did not exist. BuildKeyTable
    // returned Array.Empty when the key list was empty, so a deployment configured only
    // with the legacy single key had it silently rejected — while the XML docs claimed
    // otherwise. These pin the fallback for every construction path.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task LegacySingleKey_OnlyApiKeyConfigured_IsAccepted()
    {
        var cfg = new WebApiConfig { ApiKey = "hc_legacy_only" };

        bool nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            keys: null); // exercises the non-DI path too

        var ctx = NewApiRequest("/api/chat", "hc_legacy_only");
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.Equal(ApiKeyRole.Contribute, ctx.Items[ApiKeyMiddleware.RoleItemKey]);
    }

    [Fact]
    public async Task LegacySingleKey_RejectedByThreeArgConstructor_Path()
    {
        // The 3-arg ctor previously left _expectedKeyBytes null (CS8618) and InvokeAsync
        // read that null as "no keys configured" -> open access. Now it resolves keys.
        var cfg = new WebApiConfig { ApiKey = "hc_legacy_3arg" };

        var middleware = new ApiKeyMiddleware(
            _ => Task.CompletedTask,
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance);

        var ctx = NewApiRequest("/api/chat", "hc_legacy_3arg");
        await middleware.InvokeAsync(ctx);

        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal(ApiKeyRole.Contribute, ctx.Items[ApiKeyMiddleware.RoleItemKey]);
    }

    [Fact]
    public async Task LegacyKey_IgnoredWhenApiKeysPresent()
    {
        // task_097 contract: a non-empty ApiKeys list wins; the legacy key is not additive.
        var cfg = new WebApiConfig
        {
            ApiKey = "hc_legacy_ignored",
            ApiKeys = new List<ApiKeyEntry> { new() { Key = "hc_primary", Role = ApiKeyRole.System } },
        };

        var middleware = new ApiKeyMiddleware(
            _ => Task.CompletedTask,
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        var rejected = NewApiRequest("/api/chat", "hc_legacy_ignored");
        await middleware.InvokeAsync(rejected);
        Assert.Equal(401, rejected.Response.StatusCode);
    }

    [Fact]
    public async Task NoKeysConfigured_OpenAccessButStillRejectsNothing()
    {
        // Documented local-dev mode. The middleware must not fabricate a key table; this
        // pins the behaviour so the loud ctor log is the operator's signal.
        var cfg = new WebApiConfig();

        bool nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            keys: null);

        var ctx = NewApiRequest("/api/chat", "anything");
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HealthAndOptions_BypassAuth_EvenWithKeysConfigured()
    {
        var cfg = new WebApiConfig
        {
            ApiKeys = new List<ApiKeyEntry> { new() { Key = "hc_k", Role = ApiKeyRole.System } },
        };

        var middleware = new ApiKeyMiddleware(
            _ => Task.CompletedTask,
            cfg,
            NullLogger<ApiKeyMiddleware>.Instance,
            cfg.ApiKeys);

        var health = new DefaultHttpContext();
        health.Request.Path = "/api/health";
        await middleware.InvokeAsync(health);
        Assert.Equal(200, health.Response.StatusCode);

        var options = new DefaultHttpContext();
        options.Request.Method = "OPTIONS";
        options.Request.Path = "/api/chat";
        await middleware.InvokeAsync(options);
        Assert.Equal(200, options.Response.StatusCode);
    }

    // -----------------------------------------------------------------------
    //  Stage 6.3: on the DI path the key set is read live from the store, so a
    //  role edit through /api/auth/keys applies without restarting the agent.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DiPath_KeySetSwappedInTheStore_AppliesWithoutReconstruction()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hercules-mw-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new ApiKeyStore(
                new StorageConfig { DataRoot = tempDir },
                NullLogger<ApiKeyStore>.Instance);

            var cfg = new WebApiConfig
            {
                ApiKeys = new List<ApiKeyEntry>
                {
                    new() { Key = "key-original", Role = ApiKeyRole.Contribute }
                }
            };

            var sessions = new StudioSessionStore(cfg, NullLogger<StudioSessionStore>.Instance);
            var middleware = new ApiKeyMiddleware(
                _ => Task.CompletedTask,
                cfg,
                NullLogger<ApiKeyMiddleware>.Instance,
                store,
                sessions);

            // The key is accepted and carries the original role.
            var before = NewApiRequest("/api/config", "key-original");
            await middleware.InvokeAsync(before);
            Assert.Equal(ApiKeyRole.Contribute, before.Items[ApiKeyMiddleware.RoleItemKey]);

            // Operator promotes the key through the roles editor.
            store.SaveAndActivate(new List<ApiKeyEntry>
            {
                new() { Key = "key-original", Role = ApiKeyRole.System }
            });

            // Same middleware instance, no restart: the new role must be observed.
            var after = NewApiRequest("/api/config", "key-original");
            await middleware.InvokeAsync(after);
            Assert.Equal(ApiKeyRole.System, after.Items[ApiKeyMiddleware.RoleItemKey]);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task DiPath_RemovedKey_IsRejectedOnTheNextRequest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hercules-mw-removed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new ApiKeyStore(
                new StorageConfig { DataRoot = tempDir },
                NullLogger<ApiKeyStore>.Instance);

            var cfg = new WebApiConfig
            {
                ApiKeys = new List<ApiKeyEntry>
                {
                    new() { Key = "key-going", Role = ApiKeyRole.System },
                    new() { Key = "key-stays", Role = ApiKeyRole.System }
                }
            };

            var sessions = new StudioSessionStore(cfg, NullLogger<StudioSessionStore>.Instance);
            var middleware = new ApiKeyMiddleware(
                _ => Task.CompletedTask,
                cfg,
                NullLogger<ApiKeyMiddleware>.Instance,
                store,
                sessions);

            store.SaveAndActivate(new List<ApiKeyEntry>
            {
                new() { Key = "key-stays", Role = ApiKeyRole.System }
            });

            var removed = NewApiRequest("/api/config", "key-going");
            await middleware.InvokeAsync(removed);
            Assert.Equal(StatusCodes.Status401Unauthorized, removed.Response.StatusCode);

            var kept = NewApiRequest("/api/config", "key-stays");
            await middleware.InvokeAsync(kept);
            Assert.Equal(ApiKeyRole.System, kept.Items[ApiKeyMiddleware.RoleItemKey]);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }
}