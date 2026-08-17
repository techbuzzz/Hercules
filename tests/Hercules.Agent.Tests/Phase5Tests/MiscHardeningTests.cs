using System.Reflection;
using Hercules.Backup;
using Hercules.Budget;
using Hercules.Config;
using Hercules.Offline;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Phase5Tests;

/// <summary>
///     Unit tests for task_087 — misc hardening fixes (passphrase, MaxSizeMb,
///     NetworkMonitor fallback, DelegationBoundary TTL, IntentRouter logging).
///     Each test exercises one sub-task acceptance criterion.
/// </summary>
public class MiscHardeningTests
{
    [Fact]
    public void BackupConfig_RequirePassphrase_DefaultsToFalse()
    {
        // [task_087] Production deployments flip this to true to fail-fast
        // when HERCULES_BACKUP_PASSPHRASE is missing.
        var cfg = new BackupConfig();
        Assert.False(cfg.RequirePassphrase);
        Assert.Equal(string.Empty, cfg.Passphrase);
    }

    [Fact]
    public void OfflineSyncConfig_NetworkFallbackPollUrl_HasSafeDefault()
    {
        // [task_087] The default fallback is Cloudflare 1.1.1.1, which is
        // reachable in essentially every network and very small payload.
        var cfg = new OfflineSyncConfig();
        Assert.False(string.IsNullOrWhiteSpace(cfg.NetworkFallbackPollUrl));
        Assert.Equal("https://1.1.1.1", cfg.NetworkFallbackPollUrl);
    }

    [Fact]
    public void DelegationBoundaryConfig_ChainContextTtlSec_DefaultsTo300()
    {
        // [task_087] 5 minute default keeps long delegation chains available
        // for late-joining hops while preventing unbounded growth.
        var cfg = new DelegationBoundaryConfig();
        Assert.Equal(300, cfg.ChainContextTtlSec);
    }

    [Fact]
    public void NetworkMonitor_ResolveUrl_FallsBackToFallbackPollUrl_WhenPrimaryEmpty()
    {
        // [task_087] White-box test on the private ResolveUrl() method.
        var cfg = new OfflineSyncConfig
        {
            NetworkPollUrl = "",
            NetworkFallbackPollUrl = "https://example.invalid/health"
        };
        var monitor = new NetworkMonitor(cfg, NullLogger<NetworkMonitor>.Instance, http: new HttpClient());

        var method = typeof(NetworkMonitor).GetMethod(
            "ResolveUrl",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var url = (string?)method!.Invoke(monitor, null);
        Assert.Equal("https://example.invalid/health", url);
    }

    [Fact]
    public void NetworkMonitor_ResolveUrl_ReturnsNull_WhenBothEmpty()
    {
        var cfg = new OfflineSyncConfig
        {
            NetworkPollUrl = "",
            NetworkFallbackPollUrl = ""
        };
        var monitor = new NetworkMonitor(cfg, NullLogger<NetworkMonitor>.Instance, http: new HttpClient());

        var method = typeof(NetworkMonitor).GetMethod(
            "ResolveUrl",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var url = (string?)method!.Invoke(monitor, null);
        Assert.Null(url);
    }

    [Fact]
    public void NetworkMonitor_ResolveUrl_PrefersExplicitPollUrl()
    {
        var cfg = new OfflineSyncConfig
        {
            NetworkPollUrl = "https://primary.example/health",
            NetworkFallbackPollUrl = "https://fallback.example/health"
        };
        var monitor = new NetworkMonitor(cfg, NullLogger<NetworkMonitor>.Instance, http: new HttpClient());

        var method = typeof(NetworkMonitor).GetMethod(
            "ResolveUrl",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var url = (string?)method!.Invoke(monitor, null);
        Assert.Equal("https://primary.example/health", url);
    }

    [Fact]
    public void DelegationBoundaryService_CleanupExpiredChainContexts_EvictsStale()
    {
        // [task_087] Insert a context, force its UpdatedUtc into the past,
        // and verify that CleanupExpiredChainContexts() removes it while
        // leaving fresh entries alone.
        var cfg = new DelegationBoundaryConfig
        {
            Enabled = true,
            ChainContextTtlSec = 60
        };
        var svc = new DelegationBoundaryService(cfg, NullLogger<DelegationBoundaryService>.Instance);

        // Add a hop → creates the chain context with UpdatedUtc = now.
        svc.RecordHopCompletion("agent-a", 1, 0.01m, 100, rootRequestId: "stale-chain");

        var fresh = svc.GetChainContext("stale-chain");
        Assert.NotNull(fresh);

        // Push UpdatedUtc into the past via reflection (white-box).
        var ctxField = typeof(DelegationBoundaryService).GetField(
            "_chainContexts",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, DelegationBoundaryContext>)ctxField!.GetValue(svc)!;
        var stale = dict["stale-chain"];
        stale.UpdatedUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);

        var evicted = svc.CleanupExpiredChainContexts();
        Assert.Equal(1, evicted);
        Assert.Null(svc.GetChainContext("stale-chain"));
    }

    [Fact]
    public void DelegationBoundaryService_CleanupExpiredChainContexts_NoOpWhenTtlDisabled()
    {
        // TTL=0 disables eviction — context should survive any age.
        var cfg = new DelegationBoundaryConfig
        {
            Enabled = true,
            ChainContextTtlSec = 0
        };
        var svc = new DelegationBoundaryService(cfg, NullLogger<DelegationBoundaryService>.Instance);

        svc.RecordHopCompletion("agent-a", 1, 0.01m, 100, rootRequestId: "immortal");
        var ctxField = typeof(DelegationBoundaryService).GetField(
            "_chainContexts",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, DelegationBoundaryContext>)ctxField!.GetValue(svc)!;
        dict["immortal"].UpdatedUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(365);

        var evicted = svc.CleanupExpiredChainContexts();
        Assert.Equal(0, evicted);
        Assert.NotNull(svc.GetChainContext("immortal"));
    }

    [Fact]
    public void DelegationBoundaryService_CleanupExpiredChainContexts_KeepsFreshEntries()
    {
        var cfg = new DelegationBoundaryConfig
        {
            Enabled = true,
            ChainContextTtlSec = 60
        };
        var svc = new DelegationBoundaryService(cfg, NullLogger<DelegationBoundaryService>.Instance);

        svc.RecordHopCompletion("agent-a", 1, 0.01m, 100, rootRequestId: "fresh-1");
        svc.RecordHopCompletion("agent-b", 2, 0.02m, 200, rootRequestId: "fresh-2");

        var evicted = svc.CleanupExpiredChainContexts();
        Assert.Equal(0, evicted);
        Assert.NotNull(svc.GetChainContext("fresh-1"));
        Assert.NotNull(svc.GetChainContext("fresh-2"));
    }
}
