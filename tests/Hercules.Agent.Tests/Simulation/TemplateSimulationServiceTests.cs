using Hercules.Config;
using Hercules.Skills;
using Hercules.Simulation;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Simulation;

/// <summary>
///     Tests for TemplateSimulationService (task_031).
///     Covers: session lifecycle, replay with/without failures, coverage, edge cases.
/// </summary>
public class TemplateSimulationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ISensorSimulator> _simulator = new();
    private readonly FailureScenarioEngine _engine = new(NullLogger<FailureScenarioEngine>.Instance);
    private readonly AgentTemplateManager _templateManager;
    private readonly TemplateSimulationService _service;

    // Fixed session start for deterministic timestamp alignment in tests.
    private static readonly DateTime SimStart = new(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);

    public TemplateSimulationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hctpl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var templatesDir = Path.Combine(_tempDir, "Templates");
        Directory.CreateDirectory(templatesDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        var repo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        var packager = new SkillPackager(repo);
        _templateManager = new AgentTemplateManager(storageCfg, packager);

        _service = new TemplateSimulationService(
            _simulator.Object,
            _engine,
            _templateManager,
            NullLogger<TemplateSimulationService>.Instance);
    }

    public void Dispose()
    {
        _service.StopSession("greenhouse");
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    /// <summary>
    ///     Creates ISO 860.1 timestamps relative to SimStart.
    ///     Uses DateTimeOffset so the string is always UTC with zero offset.
    /// </summary>
    private static string Ts(double secondsFromSimStart)
        => new DateTimeOffset(SimStart.AddSeconds(secondsFromSimStart)).ToString("O");

    [Fact]
    public void HasFixtures_DelegatesToSimulator()
    {
        _simulator.Setup(s => s.HasFixtures("greenhouse")).Returns(true);
        _simulator.Setup(s => s.HasFixtures("missing")).Returns(false);

        Assert.True(_service.HasFixtures("greenhouse"));
        Assert.False(_service.HasFixtures("missing"));
    }

    [Fact]
    public void StartSession_LoadsFixturesAndSetsRunning()
    {
        var sensors = new List<SensorReading>
        {
            new SensorReading(Ts(0), "temp", 20.0, "°C", null)
        };
        var events = new List<EventFixture>
        {
            new EventFixture(Ts(0), "startup", "desc", "info")
        };
        var failures = new List<FailureScenario>
        {
            new FailureScenario("f1", "Test", "desc", "greenhouse", FailureType.StuckAt, ["temp"], 60, 300, 99.0)
        };

        _simulator.Setup(s => s.LoadSensorFixtures("greenhouse")).Returns(sensors);
        _simulator.Setup(s => s.LoadEventFixtures("greenhouse")).Returns(events);
        _simulator.Setup(s => s.LoadFailureScenarios("greenhouse")).Returns(failures);

        var session = _service.StartSession("greenhouse");

        Assert.Equal("greenhouse", session.TemplateName);
        Assert.True(session.IsRunning);
        Assert.Single(session.SensorFixtures);
        Assert.Single(session.EventFixtures);
        Assert.Single(session.FailureScenarios);
        Assert.NotNull(session.StartedAt);
    }

    [Fact]
    public void StartSession_AlreadyRunning_ReturnsExisting()
    {
        _simulator.Setup(s => s.LoadSensorFixtures(It.IsAny<string>())).Returns([]);
        _simulator.Setup(s => s.LoadEventFixtures(It.IsAny<string>())).Returns([]);
        _simulator.Setup(s => s.LoadFailureScenarios(It.IsAny<string>())).Returns([]);

        var session1 = _service.StartSession("greenhouse");
        var session2 = _service.StartSession("greenhouse");

        Assert.Same(session1, session2);
    }

    [Fact]
    public void RunReplay_WithFailures_AppliesInjection()
    {
        // Fixture timestamps are relative to SimStart (0s and 300s).
        // Session StartedAt is also aligned to SimStart via reflection.
        var sensors = new List<SensorReading>
        {
            new SensorReading(Ts(0), "temp", 20.0, "°C", null),   // 0s — before window
            new SensorReading(Ts(300), "temp", 21.0, "°C", null), // 300s — within window (100–1100s)
        };
        var failures = new List<FailureScenario>
        {
            new FailureScenario("f1", "Stuck", "desc", "greenhouse", FailureType.StuckAt,
                ["temp"], 100, 1000, 99.0)
        };

        _simulator.Setup(s => s.LoadSensorFixtures("greenhouse")).Returns(sensors);
        _simulator.Setup(s => s.LoadEventFixtures("greenhouse")).Returns([]);
        _simulator.Setup(s => s.LoadFailureScenarios("greenhouse")).Returns(failures);

        _service.StartSession("greenhouse", SimStart); // align with fixture timestamps
        var result = _service.RunReplay("greenhouse", withFailures: true);

        Assert.Equal("greenhouse", result.TemplateName);
        Assert.Equal(2, result.Readings.Count);
        Assert.Equal(20.0, result.Readings[0].Value); // not injected (0s < 100s)
        Assert.Equal(99.0, result.Readings[1].Value); // injected (300s within 100–1100s)
        Assert.Single(result.AppliedFailures);
        Assert.Equal("f1", result.AppliedFailures[0].FailureId);
        Assert.Equal(1, result.Metrics.FailuresInjected);
    }

    [Fact]
    public void RunReplay_NoFailures_OriginalReadings()
    {
        var sensors = new List<SensorReading>
        {
            new SensorReading(Ts(0), "temp", 20.0, "°C", null),
        };

        _simulator.Setup(s => s.LoadSensorFixtures("greenhouse")).Returns(sensors);
        _simulator.Setup(s => s.LoadEventFixtures("greenhouse")).Returns([]);
        _simulator.Setup(s => s.LoadFailureScenarios("greenhouse")).Returns([]);

        _service.StartSession("greenhouse", SimStart);
        var result = _service.RunReplay("greenhouse", withFailures: false);

        Assert.Equal(20.0, result.Readings[0].Value);
        Assert.Equal(0, result.Metrics.FailuresInjected);
    }

    [Fact]
    public void RunReplay_GeneratesFailureEvents()
    {
        var sensors = new List<SensorReading>
        {
            new SensorReading(Ts(0), "temp", 20.0, "°C", null),
        };
        var existingEvents = new List<EventFixture>
        {
            new EventFixture(Ts(0), "startup", "System started", "info")
        };
        var failures = new List<FailureScenario>
        {
            new FailureScenario("f1", "Stuck", "desc", "greenhouse", FailureType.StuckAt, ["temp"], 60, 120, 99.0)
        };

        _simulator.Setup(s => s.LoadSensorFixtures("greenhouse")).Returns(sensors);
        _simulator.Setup(s => s.LoadEventFixtures("greenhouse")).Returns(existingEvents);
        _simulator.Setup(s => s.LoadFailureScenarios("greenhouse")).Returns(failures);

        _service.StartSession("greenhouse", SimStart);
        var result = _service.RunReplay("greenhouse", withFailures: true);

        Assert.Equal(3, result.Events.Count);
        Assert.Contains(result.Events, e => e.Type == "failure_injected");
        Assert.Contains(result.Events, e => e.Type == "failure_resolved");
    }

    [Fact]
    public void RunReplay_NoSession_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _service.RunReplay("missing"));
    }

    [Fact]
    public void StopSession_ClearsRunning()
    {
        _simulator.Setup(s => s.LoadSensorFixtures(It.IsAny<string>())).Returns([]);
        _simulator.Setup(s => s.LoadEventFixtures(It.IsAny<string>())).Returns([]);
        _simulator.Setup(s => s.LoadFailureScenarios(It.IsAny<string>())).Returns([]);

        _service.StartSession("greenhouse");
        _service.StopSession("greenhouse");

        var session = _service.GetSession("greenhouse");
        Assert.False(session?.IsRunning);
    }

    [Fact]
    public void GetSession_ReturnsSession()
    {
        _simulator.Setup(s => s.LoadSensorFixtures(It.IsAny<string>())).Returns([]);
        _simulator.Setup(s => s.LoadEventFixtures(It.IsAny<string>())).Returns([]);
        _simulator.Setup(s => s.LoadFailureScenarios(It.IsAny<string>())).Returns([]);

        _service.StartSession("greenhouse");

        var session = _service.GetSession("greenhouse");
        Assert.NotNull(session);
        Assert.Equal("greenhouse", session.TemplateName);
    }

    [Fact]
    public void GetSession_Missing_ReturnsNull()
    {
        Assert.Null(_service.GetSession("nonexistent"));
    }

    [Fact]
    public void GetFailureCoverage_DelegatesToEngine()
    {
        var failures = new List<FailureScenario>
        {
            new FailureScenario("f1", "Stuck", "desc", "greenhouse", FailureType.StuckAt, ["temp", "humidity"], 60, 300, 50.0)
        };
        _simulator.Setup(s => s.LoadFailureScenarios("greenhouse")).Returns(failures);

        var coverage = _service.GetFailureCoverage("greenhouse");

        Assert.Equal(2, coverage.Count);
        Assert.Single(coverage["temp"]);
    }

    [Fact]
    public void GetSimulatableTemplates_EmptyWhenNoTemplates()
    {
        var result = _service.GetSimulatableTemplates();
        Assert.Empty(result);
    }
}
