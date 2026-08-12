using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Hercules.Tools.Approval;
using Xunit;

namespace Hercules.Agent.Tests.Tools;

public class ApprovalServiceTests : IDisposable
{
    private readonly ApprovalConfig _config;
    private readonly Mock<ILogger<ApprovalService>> _loggerMock;
    private readonly SqliteSessionStore _store;
    private readonly ApprovalService _svc;

    public ApprovalServiceTests()
    {
        _config = new ApprovalConfig { Enabled = true, DefaultTtlMinutes = 30, MaxPending = 50 };
        _loggerMock = new Mock<ILogger<ApprovalService>>();
        _store = new SqliteSessionStore(new StorageConfig { DataRoot = Path.Combine(Path.GetTempPath(), $"hercules_tests_{Guid.NewGuid():N}"), SqliteFile = "test.db" });
        _svc = new ApprovalService(_config, _store, _loggerMock.Object);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task RequestAsync_CreatesPendingRequest()
    {
        var result = await _svc.RequestAsync("sess1", "http_tool", "{}", "test reason");

        Assert.NotEmpty(result.RequestId);
        Assert.Equal("sess1", result.SessionId);
        Assert.Equal("http_tool", result.ToolName);
        Assert.Equal("test reason", result.Reason);
        Assert.Equal(ApprovalStatus.Pending, result.Status);
    }

    [Fact]
    public async Task RequestAsync_Disabled_ReturnsApprovedAuto()
    {
        var disabledConfig = new ApprovalConfig { Enabled = false };
        var disabled = new ApprovalService(disabledConfig, _store, _loggerMock.Object);

        var result = await disabled.RequestAsync("sess1", "http_tool", "{}", "test reason");

        Assert.Equal(ApprovalStatus.Approved, result.Status);
        Assert.StartsWith("auto-", result.RequestId);
    }

    [Fact]
    public async Task ApproveAsync_SetsStatusToApproved()
    {
        var req = await _svc.RequestAsync("sess1", "http_tool", "{}", "reason");
        var ok = await _svc.ApproveAsync(req.RequestId);

        Assert.True(ok);
        var cached = _svc.Get(req.RequestId);
        Assert.NotNull(cached);
        Assert.Equal(ApprovalStatus.Approved, cached.Status);
    }

    [Fact]
    public async Task DenyAsync_SetsStatusToDenied()
    {
        var req = await _svc.RequestAsync("sess1", "http_tool", "{}", "reason");
        var ok = await _svc.DenyAsync(req.RequestId);

        Assert.True(ok);
        var cached = _svc.Get(req.RequestId);
        Assert.NotNull(cached);
        Assert.Equal(ApprovalStatus.Denied, cached.Status);
    }

    [Fact]
    public async Task ApproveAsync_NonExistent_ReturnsFalse()
    {
        var ok = await _svc.ApproveAsync("nonexistent_id");
        Assert.False(ok);
    }

    [Fact]
    public async Task DenyAsync_NonExistent_ReturnsFalse()
    {
        var ok = await _svc.DenyAsync("nonexistent_id");
        Assert.False(ok);
    }

    [Fact]
    public async Task IsApproved_ApprovedRequest_ReturnsTrue()
    {
        var req = await _svc.RequestAsync("sess1", "http_tool", "{}", "reason");
        await _svc.ApproveAsync(req.RequestId);

        var approved = _svc.IsApproved("http_tool", "sess1");
        Assert.True(approved);
    }

    [Fact]
    public async Task IsApproved_PendingRequest_ReturnsFalse()
    {
        await _svc.RequestAsync("sess1", "http_tool", "{}", "reason");

        var approved = _svc.IsApproved("http_tool", "sess1");
        Assert.False(approved);
    }

    [Fact]
    public async Task IsApproved_DifferentSession_ReturnsFalse()
    {
        var req = await _svc.RequestAsync("sess1", "http_tool", "{}", "reason");
        await _svc.ApproveAsync(req.RequestId);

        var approved = _svc.IsApproved("http_tool", "sess2");
        Assert.False(approved);
    }

    [Fact]
    public async Task IsApproved_DifferentTool_ReturnsFalse()
    {
        var req = await _svc.RequestAsync("sess1", "http_tool", "{}", "reason");
        await _svc.ApproveAsync(req.RequestId);

        var approved = _svc.IsApproved("other_tool", "sess1");
        Assert.False(approved);
    }

    [Fact]
    public async Task GetPending_ReturnsOnlyPending()
    {
        var req1 = await _svc.RequestAsync("sess1", "tool1", "{}", "r1");
        var req2 = await _svc.RequestAsync("sess1", "tool2", "{}", "r2");
        await _svc.ApproveAsync(req1.RequestId);

        var pending = _svc.GetPending();
        Assert.Single(pending);
        Assert.Equal(req2.RequestId, pending[0].RequestId);
    }

    [Fact]
    public async Task GetPending_WithSessionFilter_ReturnsOnlyMatchingSession()
    {
        await _svc.RequestAsync("sess1", "tool1", "{}", "r1");
        await _svc.RequestAsync("sess2", "tool2", "{}", "r2");

        var sess1Pending = _svc.GetPending("sess1");
        var sess2Pending = _svc.GetPending("sess2");

        Assert.Single(sess1Pending);
        Assert.Single(sess2Pending);
        Assert.Equal("tool1", sess1Pending[0].ToolName);
        Assert.Equal("tool2", sess2Pending[0].ToolName);
    }

    [Fact]
    public async Task Get_ReturnsCachedResult()
    {
        var req = await _svc.RequestAsync("sess1", "http_tool", "{}", "reason");
        var found = _svc.Get(req.RequestId);

        Assert.NotNull(found);
        Assert.Equal(req.RequestId, found.RequestId);
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var found = _svc.Get("nonexistent_id");
        Assert.Null(found);
    }

    [Fact]
    public async Task ExpireOldApprovals_MarksExpired()
    {
        var veryShortConfig = new ApprovalConfig { Enabled = true, DefaultTtlMinutes = -1 }; // immediately expired
        var shortSvc = new ApprovalService(veryShortConfig, _store, _loggerMock.Object);

        await shortSvc.RequestAsync("sess1", "http_tool", "{}", "reason");
        await shortSvc.ExpireOldApprovalsAsync();

        var pending = shortSvc.GetPending();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task ApproveThenDeny_FirstSucceeds_SecondFails()
    {
        var req = await _svc.RequestAsync("sess1", "http_tool", "{}", "reason");
        var ok1 = await _svc.ApproveAsync(req.RequestId);
        var ok2 = await _svc.DenyAsync(req.RequestId);

        Assert.True(ok1);
        Assert.False(ok2); // already approved
    }

    [Fact]
    public async Task MultipleRequestsForSameSession_AllTracked()
    {
        await _svc.RequestAsync("sess1", "tool1", "{}", "r1");
        await _svc.RequestAsync("sess1", "tool2", "{}", "r2");
        await _svc.RequestAsync("sess2", "tool3", "{}", "r3");

        var all = _svc.GetPending();
        Assert.Equal(3, all.Count);

        var sess1 = _svc.GetPending("sess1");
        Assert.Equal(2, sess1.Count);
    }
}
