using Hercules.Mesh.Backends.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     Tests for the LISTEN reconnect-with-backoff loop added in
///     task_082 (H15). Without a live PostgreSQL instance we can only
///     exercise the loop from the outside: ensure that subscribe calls
///     don't block indefinitely on an unreachable host, that the bus
///     tolerates a flurry of connect failures, and that disposal
///     terminates the background loop cleanly.
/// </summary>
public class PostgresMeshBusReconnectTests
{
    [Fact]
    public async Task Subscribe_OnUnreachableHost_DoesNotBlockAndDisposeCleansUp()
    {
        // Bad host / port so every connect attempt fails fast.
        var cfg = new PostgresMeshConfig
        {
            Enabled = true,
            ConnectionString = "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=postgres;Timeout=1;Command Timeout=1",
            AutoCreateSchema = false
        };
        await using var dataSource = new NpgsqlDataSourceBuilder(cfg.ConnectionString).Build();

        using var bus = new PostgresMeshBus(dataSource, cfg, NullLogger<PostgresMeshBus>.Instance);

        // Fire a subscribe; the LISTEN loop will fail on connect and then
        // back off in the background. We assert the call returns (no
        // hang) and the bus is healthy-enough to dispose within a few
        // seconds.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sub = await bus.SubscribeAsync(
            "reconnect-test-topic",
            (_, _) => Task.CompletedTask);
        sw.Stop();

        Assert.NotNull(sub);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"SubscribeAsync took {sw.Elapsed} — should not block on unreachable host");

        // Dispose must terminate the reconnect loop cleanly. The
        // HttpServer / mesh pipeline relies on Dispose returning within
        // a bounded time so graceful shutdown is not held up.
        sw.Restart();
        bus.Dispose();
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"Dispose took {sw.Elapsed} — reconnect loop should observe _disposed and exit");
    }

    [Fact]
    public async Task RequestReply_OnUnreachableHost_DoesNotHang()
    {
        // Bad host / port so every connect attempt fails fast.
        var cfg = new PostgresMeshConfig
        {
            Enabled = true,
            ConnectionString = "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=postgres;Timeout=1;Command Timeout=1",
            AutoCreateSchema = false
        };
        await using var dataSource = new NpgsqlDataSourceBuilder(cfg.ConnectionString).Build();

        using var bus = new PostgresMeshBus(dataSource, cfg, NullLogger<PostgresMeshBus>.Instance);

        var envelope = new Hercules.Mesh.IntentEnvelope
        {
            RequestId = Guid.NewGuid().ToString("N")
        };

        // We don't care about the result here — we just want to confirm
        // that the request does not block the test thread forever when
        // the underlying LISTEN loop is failing to connect. The
        // 1-second timeout ensures the call returns even if the bus
        // never makes a connection.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await bus.RequestReplyAsync("target-agent", envelope, default, TimeSpan.FromSeconds(1)));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"RequestReplyAsync took {sw.Elapsed} — should fail-fast or timeout");
    }
}
