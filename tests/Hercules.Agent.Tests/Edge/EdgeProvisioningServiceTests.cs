using Hercules.Audit;
using Hercules.Config;
using Hercules.Edge;
using Hercules.Security;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Edge;

/// <summary>
///     Тесты EdgeProvisioningService (task_059):
///     enrollment, idempotency, secure defaults, device ID resolution.
/// </summary>
public class EdgeProvisioningServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly EdgeConfig _edgeConfig;
    private readonly SecurityOpsConfig _securityConfig;
    private readonly StorageConfig _storageConfig;
    private readonly Mock<IFleetIdentityService> _fleetIdentityMock;
    private readonly Mock<ICertificateService> _certificateMock;
    private readonly Mock<IAuditService> _auditMock;
    private readonly Mock<ILogger<EdgeProvisioningService>> _loggerMock;

    public EdgeProvisioningServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-edge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "security"));

        _edgeConfig = new EdgeConfig
        {
            EnrolmentUrl = "",
            EnrolmentToken = "",
            DeviceId = "test-device-001",
            Enrolled = false,
            EnrollmentStatePath = Path.Combine(_tempDir, "security", "enrollment.json")
        };

        _securityConfig = new SecurityOpsConfig
        {
            IdentityStoragePath = Path.Combine(_tempDir, "security", "identity"),
            CertificateStoragePath = Path.Combine(_tempDir, "security", "certs"),
            AutoRotateIdentity = true
        };

        _storageConfig = new StorageConfig
        {
            DataRoot = _tempDir
        };

        _fleetIdentityMock = new Mock<IFleetIdentityService>();
        _fleetIdentityMock.Setup(x => x.GetCurrentIdentityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleetIdentity("test-agent", "cred_test123",
                DateTime.UtcNow, DateTime.UtcNow.AddDays(90), "fp_test123abc", Array.Empty<string>()));

        _certificateMock = new Mock<ICertificateService>();
        _certificateMock.Setup(x => x.GetCurrentCertificateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CertificateInfo("cert_001", "CN=test", "CN=test",
                DateTime.UtcNow, DateTime.UtcNow.AddDays(365), "thumb_test123", "RSA", 2048, true));
        _certificateMock.Setup(x => x.CheckRenewalNeededAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RenewalCheckResult(false, false, 60, null));

        _auditMock = new Mock<IAuditService>();
        _auditMock.Setup(x => x.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _loggerMock = new Mock<ILogger<EdgeProvisioningService>>();
    }

    [Fact]
    public void IsEnrolled_WhenNotEnrolled_ReturnsFalse()
    {
        var sut = BuildService();
        Assert.False(sut.IsEnrolled);
    }

    [Fact]
    public async Task EnsureEnrolledAsync_NotPreviouslyEnrolled_Succeeds()
    {
        var sut = BuildService();

        var result = await sut.EnsureEnrolledAsync();

        Assert.True(result.Success);
        Assert.False(result.WasAlreadyEnrolled);
        Assert.Equal("test-device-001", result.DeviceId);
        Assert.NotNull(result.EnrolmentToken);
        Assert.NotEmpty(result.EnrolmentToken);
    }

    [Fact]
    public async Task EnsureEnrolledAsync_AlreadyEnrolled_ReturnsCachedResult()
    {
        var sut = BuildService();

        // First enrollment
        var first = await sut.EnsureEnrolledAsync();
        Assert.True(first.Success);

        // Second call — should be idempotent
        var second = await sut.EnsureEnrolledAsync();

        Assert.True(second.Success);
        Assert.True(second.WasAlreadyEnrolled);
        Assert.Equal(first.DeviceId, second.DeviceId);
    }

    [Fact]
    public async Task EnsureEnrolledAsync_CreatesEnrollmentStateFile()
    {
        var sut = BuildService();

        await sut.EnsureEnrolledAsync();

        Assert.True(File.Exists(_edgeConfig.EnrollmentStatePath));
        var json = await File.ReadAllTextAsync(_edgeConfig.EnrollmentStatePath);
        Assert.Contains("test-device-001", json);
        Assert.Contains("enrolled", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureEnrolledAsync_CallsFleetIdentityService()
    {
        var sut = BuildService();

        await sut.EnsureEnrolledAsync();

        _fleetIdentityMock.Verify(x => x.GetCurrentIdentityAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureEnrolledAsync_CallsCertificateService()
    {
        var sut = BuildService();

        await sut.EnsureEnrolledAsync();

        _certificateMock.Verify(x => x.GetCurrentCertificateAsync(It.IsAny<CancellationToken>()), Times.Once);
        _certificateMock.Verify(x => x.CheckRenewalNeededAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureEnrolledAsync_AuditsEnrollment()
    {
        var sut = BuildService();

        await sut.EnsureEnrolledAsync();

        _auditMock.Verify(x => x.LogAsync(
            "edge-provisioning",
            "device_enrolled",
            "test-device-001",
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDeviceIdentityAsync_AfterEnrollment_ReturnsIdentity()
    {
        var sut = BuildService();
        await sut.EnsureEnrolledAsync();

        var identity = await sut.GetDeviceIdentityAsync();

        Assert.NotNull(identity);
        Assert.Equal("test-device-001", identity.DeviceId);
        Assert.NotEmpty(identity.EnrolmentToken);
    }

    [Fact]
    public async Task GetDeviceIdentityAsync_BeforeEnrollment_ReturnsNull()
    {
        var sut = BuildService();

        var identity = await sut.GetDeviceIdentityAsync();

        Assert.Null(identity);
    }

    [Fact]
    public async Task MarkEnrolledAsync_WhenNotEnrolled_Enrolls()
    {
        var sut = BuildService();

        await sut.MarkEnrolledAsync();

        Assert.True(sut.IsEnrolled);
    }

    [Fact]
    public async Task MarkEnrolledAsync_WhenAlreadyEnrolled_IsIdempotent()
    {
        var sut = BuildService();
        await sut.MarkEnrolledAsync();

        // Should not throw
        await sut.MarkEnrolledAsync();

        Assert.True(sut.IsEnrolled);
    }

    [Fact]
    public async Task EnsureEnrolledAsync_WithoutExplicitDeviceId_UsesFallbackId()
    {
        var cfg = new EdgeConfig
        {
            EnrollmentStatePath = Path.Combine(_tempDir, "enroll2.json"),
            DeviceId = ""
        };
        var sut = new EdgeProvisioningService(
            cfg, _securityConfig, _storageConfig,
            _fleetIdentityMock.Object, _certificateMock.Object,
            _auditMock.Object, _loggerMock.Object);

        var result = await sut.EnsureEnrolledAsync();

        Assert.True(result.Success);
        Assert.NotNull(result.DeviceId);
        Assert.NotEmpty(result.DeviceId);
    }

    private EdgeProvisioningService BuildService() => new(
        _edgeConfig,
        _securityConfig,
        _storageConfig,
        _fleetIdentityMock.Object,
        _certificateMock.Object,
        _auditMock.Object,
        _loggerMock.Object);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }
}
