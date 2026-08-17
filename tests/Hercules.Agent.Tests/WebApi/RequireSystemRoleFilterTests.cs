using System.Net;
using System.Net.Http.Json;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     task_097: integration-тест для <see cref="RequireSystemRoleFilter"/> (ADR-0004).
///     Поднимаем минимальный WebApplication с фильтром и проверяем статус-коды через HTTP.
///     Аналогичный подход используется в <c>KestrelRateLimitCachePipelineTests</c>.
/// </summary>
public class RequireSystemRoleFilterTests
{
    [Fact]
    public async Task SystemRole_PassesThroughToHandler()
    {
        using var host = await BuildHost(seedRole: ApiKeyRole.System);
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/api/admin/test");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal("ok", body);
    }

    [Fact]
    public async Task ContributeRole_Returns403()
    {
        using var host = await BuildHost(seedRole: ApiKeyRole.Contribute);
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/api/admin/test");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task NoRole_Returns401()
    {
        using var host = await BuildHost(seedRole: null);
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/api/admin/test");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task FilterOnPutMethod_StillAppliesToThatMethodOnly()
    {
        // GET /api/admin/test — под фильтром
        // GET /api/admin/open — без фильтра, доступен обеим ролям
        using var host = await BuildHost(seedRole: ApiKeyRole.Contribute);
        var client = host.GetTestClient();

        var filtered = await client.GetAsync("/api/admin/test");
        var open = await client.GetAsync("/api/admin/open");

        Assert.Equal(HttpStatusCode.Forbidden, filtered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
    }

    private static async Task<IHost> BuildHost(ApiKeyRole? seedRole)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        // Подавляем логи в выводе теста.
        builder.Logging.ClearProviders();

        var app = (WebApplication)builder.Build();

        // Симулируем ApiKeyMiddleware — ставим role в HttpContext.Items.
        app.Use(async (ctx, next) =>
        {
            if (seedRole.HasValue)
            {
                ctx.Items[ApiKeyMiddleware.RoleItemKey] = seedRole.Value;
            }
            await next();
        });

        var group = app.MapGet("/api/admin/test", () => Results.Text("ok"));
        group.RequireSystemRole();

        app.MapGet("/api/admin/open", () => Results.Text("open"));

        await app.StartAsync();
        return app;
    }
}
