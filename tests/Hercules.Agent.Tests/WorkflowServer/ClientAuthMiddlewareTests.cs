using Hercules.WorkflowServer.Auth;
using Hercules.WorkflowServer.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.WorkflowServer;

/// <summary>
///     task_104: unit-тесты для <see cref="ClientAuthMiddleware"/>.
///     Проверяет: valid creds pass, invalid → 401, health bypass, no-creds open mode,
///     CORS preflight bypass.
/// </summary>
public class ClientAuthMiddlewareTests : IDisposable
{
    private readonly string _tempDir;

    public ClientAuthMiddlewareTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hercules-wfs-mw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task ValidCreds_PassesAndSetsClientId()
    {
        var cfg = NewConfig("wfs_id_1", "wfs_secret_1");
        var store = NewStore(cfg);

        bool nextCalled = false;
        var middleware = new ClientAuthMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            store, cfg, NullLogger<ClientAuthMiddleware>.Instance);

        var ctx = NewRequest("/api/workflows", "wfs_id_1", "wfs_secret_1");
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.Equal("wfs_id_1", ctx.Items[ClientAuthMiddleware.ClientIdItemKey]);
        Assert.NotEqual(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task WrongSecret_Returns401()
    {
        var cfg = NewConfig("wfs_id_1", "wfs_secret_1");
        var store = NewStore(cfg);

        var middleware = new ClientAuthMiddleware(
            _ => Task.CompletedTask,
            store, cfg, NullLogger<ClientAuthMiddleware>.Instance);

        var ctx = NewRequest("/api/workflows", "wfs_id_1", "wrong");
        await middleware.InvokeAsync(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task MissingHeaders_Returns401()
    {
        var cfg = NewConfig("wfs_id_1", "wfs_secret_1");
        var store = NewStore(cfg);

        var middleware = new ClientAuthMiddleware(
            _ => Task.CompletedTask,
            store, cfg, NullLogger<ClientAuthMiddleware>.Instance);

        var ctx = NewRequest("/api/workflows", null, null);
        await middleware.InvokeAsync(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_BypassesAuth()
    {
        var cfg = NewConfig("wfs_id_1", "wfs_secret_1");
        var store = NewStore(cfg);

        bool nextCalled = false;
        var middleware = new ClientAuthMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            store, cfg, NullLogger<ClientAuthMiddleware>.Instance);

        // Никаких заголовков — должно пропустить.
        var ctx = NewRequest("/api/workflows/health", null, null);
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task NoCredsConfigured_AutoGeneratesAndEnforcesAuth()
    {
        // Пустой конфиг + пустой DataRoot → store генерирует credentials, middleware их использует.
        // Это совпадает с поведением agent's ApiKeyStore (task_097): первый старт = auto-gen + persist.
        var cfg = new WorkflowServerConfig { DataRoot = _tempDir };
        var store = NewStore(cfg);

        var middleware = new ClientAuthMiddleware(
            _ => Task.CompletedTask,
            store, cfg, NullLogger<ClientAuthMiddleware>.Instance);

        // Без заголовков — должно быть 401, так как auto-generated credentials уже активны.
        var ctx = NewRequest("/api/workflows", null, null);
        await middleware.InvokeAsync(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
        // Файл credentials должен быть создан при LoadOrGenerate.
        Assert.True(File.Exists(store.CredentialsFilePath));
    }

    [Fact]
    public async Task CorsPreflight_BypassesAuth()
    {
        var cfg = NewConfig("wfs_id_1", "wfs_secret_1");
        var store = NewStore(cfg);

        bool nextCalled = false;
        var middleware = new ClientAuthMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            store, cfg, NullLogger<ClientAuthMiddleware>.Instance);

        var ctx = NewRequest("/api/workflows", null, null, method: "OPTIONS");
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task WebhookTrigger_BypassesAuth_TokenAuthInEndpoint()
    {
        // ADR-0008: webhook авторизуется по токену в URL, а не по clientId/secret.
        var cfg = NewConfig("wfs_id_1", "wfs_secret_1");
        var store = NewStore(cfg);

        bool nextCalled = false;
        var middleware = new ClientAuthMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            store, cfg, NullLogger<ClientAuthMiddleware>.Instance);

        var ctx = NewRequest("/api/workflows/triggers/webhook/abc123", null, null, method: "POST");
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    private static WorkflowServerConfig NewConfig(string id, string secret) => new()
    {
        Auth = new AuthSection { ClientId = id, ClientSecret = secret }
    };

    private ClientCredentialStore NewStore(WorkflowServerConfig cfg) =>
        new(cfg, NullLogger<ClientCredentialStore>.Instance);

    private static DefaultHttpContext NewRequest(string path, string? id, string? secret, string method = "GET")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        if (id is not null) ctx.Request.Headers[ClientAuthMiddleware.HeaderId] = id;
        if (secret is not null) ctx.Request.Headers[ClientAuthMiddleware.HeaderSecret] = secret;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }
}
