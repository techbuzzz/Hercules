using Hercules.Config;
using Hercules.Mesh;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

/// <summary>
///     Тесты CapabilityHealthService: TTL cleanup, health-check interval,
///     consecutive failures tracking.
/// </summary>
public class CapabilityHealthServiceTests : IDisposable
{
    private readonly CapabilityRegistry _store;
    private readonly CapabilityRegistryService _service;
    private readonly Mock<ILogger<CapabilityHealthService>> _loggerMock;
    private readonly string _tempDir;

    public CapabilityHealthServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-hs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "hs_test.db");
        _store = new CapabilityRegistry(dbPath);
        _service = new CapabilityRegistryService(_store);
        _loggerMock = new Mock<ILogger<CapabilityHealthService>>();
    }

    public void Dispose()
    {
        _store.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private static HttpMessageHandler CreateMockHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFn)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) => handlerFn(req));
        return mock.Object;
    }

    private static HttpMessageHandler CreateSuccessHandler()
        => CreateMockHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));

    private static HttpMessageHandler CreateFailureHandler(int statusCode)
        => CreateMockHandler(_ => new HttpResponseMessage((System.Net.HttpStatusCode)statusCode));

    private static HttpMessageHandler CreateExceptionHandler(Exception ex)
        => CreateMockHandler(_ => throw ex);

    private CapabilityHealthService CreateService(HttpMessageHandler handler, int intervalSec = 60, int threshold = 3)
    {
        var httpClient = new HttpClient(handler);
        var config = new MeshConfig
        {
            CapabilityHealthCheckIntervalSeconds = intervalSec,
            CapabilityHealthFailureThreshold = threshold
        };
        return new CapabilityHealthService(_service, config, httpClient, _loggerMock.Object);
    }

    private void RegisterAgent(string agentId, string endpoint)
    {
        _store.Register(new AgentManifest
        {
            AgentId = agentId,
            DisplayName = agentId,
            Description = agentId,
            Endpoint = endpoint,
            Capabilities = new List<ManifestCapability>()
        });
    }

    // --- Health check ---

    [Fact]
    public async Task CheckAgentAsync_HealthyEndpoint_SetsHealthyStatus()
    {
        RegisterAgent("h-healthy", "http://healthy:5001");
        var svc = CreateService(CreateSuccessHandler());

        var entry = _service.GetEntry("h-healthy")!;
        await svc.CheckAgentAsync(entry, CancellationToken.None);

        var updated = _service.GetEntry("h-healthy");
        Assert.NotNull(updated);
        Assert.Equal("healthy", updated.HealthStatus);
    }

    [Fact]
    public async Task CheckAgentAsync_HealthyEndpoint_UpdatesLatencyHint()
    {
        RegisterAgent("h-lat", "http://lat:5001");
        var svc = CreateService(CreateSuccessHandler());

        var entry = _service.GetEntry("h-lat")!;
        await svc.CheckAgentAsync(entry, CancellationToken.None);

        var updated = _service.GetEntry("h-lat");
        Assert.NotNull(updated);
        Assert.True(updated.LatencyHintMs >= 0);
    }

    [Fact]
    public async Task CheckAgentAsync_FailedEndpoint_SetsUnreachable()
    {
        RegisterAgent("h-fail", "http://fail:5001");
        var svc = CreateService(CreateFailureHandler(503));

        var entry = _service.GetEntry("h-fail")!;
        await svc.CheckAgentAsync(entry, CancellationToken.None);

        var updated = _service.GetEntry("h-fail");
        Assert.NotNull(updated);
        Assert.True(updated.HealthStatus is "unreachable" or "unhealthy");
    }

    [Fact]
    public async Task CheckAgentAsync_HttpException_SetsUnreachable()
    {
        RegisterAgent("h-net", "http://netfail:5999");
        var svc = CreateService(CreateExceptionHandler(new HttpRequestException("Connection refused")));

        var entry = _service.GetEntry("h-net")!;
        await svc.CheckAgentAsync(entry, CancellationToken.None);

        var updated = _service.GetEntry("h-net");
        Assert.NotNull(updated);
        Assert.Equal("unreachable", updated.HealthStatus);
    }

    [Fact]
    public async Task CheckAgentAsync_NoEndpoint_DoesNothing()
    {
        _store.Register(new AgentManifest { AgentId = "no-ep", DisplayName = "no-ep", Endpoint = "", Capabilities = new List<ManifestCapability>() });
        var svc = CreateService(CreateSuccessHandler());

        var entry = _service.GetEntry("no-ep")!;
        await svc.CheckAgentAsync(entry, CancellationToken.None);

        var updated = _service.GetEntry("no-ep");
        Assert.NotNull(updated);
        Assert.Equal("unknown", updated.HealthStatus);
    }

    // --- RecordSuccess / RecordFailure ---

    [Fact]
    public void RecordSuccess_SetsHealthy()
    {
        RegisterAgent("rs-agent", "http://rs");
        var svc = CreateService(CreateSuccessHandler());

        svc.RecordSuccess("rs-agent");

        var updated = _service.GetEntry("rs-agent");
        Assert.NotNull(updated);
        Assert.Equal("healthy", updated.HealthStatus);
    }

    [Fact]
    public void RecordFailure_IncrementsConsecutiveFailures()
    {
        RegisterAgent("rf-agent", "http://rf");
        var svc = CreateService(CreateExceptionHandler(new HttpRequestException()));

        svc.RecordFailure("rf-agent");
        var first = _service.GetEntry("rf-agent");

        svc.RecordFailure("rf-agent");
        var second = _service.GetEntry("rf-agent");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(second.ConsecutiveFailures > first.ConsecutiveFailures);
    }

    [Fact]
    public void RecordFailure_ThresholdReached_SetsUnhealthy()
    {
        RegisterAgent("rf-thresh", "http://rfth");
        // threshold = 2
        var svc = CreateService(CreateExceptionHandler(new HttpRequestException()), threshold: 2);

        svc.RecordFailure("rf-thresh");
        svc.RecordFailure("rf-thresh"); // threshold reached

        var updated = _service.GetEntry("rf-thresh");
        Assert.NotNull(updated);
        Assert.Equal("unhealthy", updated.HealthStatus);
    }

    // --- RunCycleAsync (cleanup) ---

    [Fact]
    public async Task RunCycleAsync_CleansUpExpired()
    {
        RegisterAgent("cycle-keep", "http://keep");
        _service.SetExpiry("cycle-keep", 86400);
        RegisterAgent("cycle-exp", "http://exp");
        _service.SetExpiry("cycle-exp", 0);
        var svc = CreateService(CreateSuccessHandler());

        await svc.RunCycleAsync(CancellationToken.None);

        Assert.NotNull(_service.GetEntry("cycle-keep"));
        Assert.Null(_service.GetEntry("cycle-exp"));
    }

    // --- Health check interval=0: RunCycleAsync still works (no ExecuteAsync call needed) ---

    [Fact]
    public async Task RunCycleAsync_HealthyAgent_UpdatesLatency()
    {
        RegisterAgent("cycle-lat", "http://latency");
        var svc = CreateService(CreateSuccessHandler());

        await svc.RunCycleAsync(CancellationToken.None);

        var updated = _service.GetEntry("cycle-lat");
        Assert.NotNull(updated);
        Assert.True(updated.LatencyHintMs >= 0);
    }
}
