using Hercules.Config;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh;
using Hercules.Offline;
using Hercules.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Offline;

/// <summary>
///     Unit tests for task_060: Offline resilience.
///     Covers: SqliteOutboxStore, OfflineSyncService, NetworkMonitor.
/// </summary>
public class SqliteOutboxStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteSessionStore _sessionStore;
    private readonly SqliteOutboxStore _store;
    private readonly OfflineSyncConfig _config;

    public SqliteOutboxStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-offline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _sessionStore = new SqliteSessionStore(storageCfg);
        _config = new OfflineSyncConfig { MaxQueueSize = 10 };
        var loggerMock = new Mock<ILogger<SqliteOutboxStore>>();
        _store = new SqliteOutboxStore(_sessionStore, _config, loggerMock.Object);
    }

    public void Dispose()
    {
        _sessionStore.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    #region Basic enqueue / count

    [Fact]
    public async Task EnqueueAsync_SingleItem_IncreasesPendingCount()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { data = 42 });
        await _store.EnqueueAsync(item);

        Assert.Equal(1, await _store.GetPendingCountAsync());
    }

    [Fact]
    public async Task EnqueueAsync_DifferentTypes_StoredCorrectly()
    {
        await _store.EnqueueAsync(OutboxItem.SensorLog("s1", "c1", new { x = 1 }));
        await _store.EnqueueAsync(OutboxItem.TaskResult("s1", "c1", new { x = 2 }));
        await _store.EnqueueAsync(OutboxItem.Alert("s1", "c1", new { x = 3 }));

        Assert.Equal(1, await _store.GetPendingCountByTypeAsync(OutboxItemType.SensorLog));
        Assert.Equal(1, await _store.GetPendingCountByTypeAsync(OutboxItemType.TaskResult));
        Assert.Equal(1, await _store.GetPendingCountByTypeAsync(OutboxItemType.Alert));
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsSortedByPriorityAndCreatedAt()
    {
        var low = OutboxItem.SensorLog("s1", "c1", new { p = "low" });
        low.Priority = OutboxItemPriority.Low;

        var high = OutboxItem.SensorLog("s1", "c1", new { p = "high" });
        high.Priority = OutboxItemPriority.High;

        await _store.EnqueueAsync(high); // inserted first
        await _store.EnqueueAsync(low);

        var pending = (await _store.GetPendingAsync()).ToList();

        Assert.Equal(2, pending.Count);
        Assert.Equal(OutboxItemPriority.High, pending[0].Priority); // high comes first
        Assert.Equal(OutboxItemPriority.Low, pending[1].Priority);
    }

    #endregion

    #region Deduplication

    [Fact]
    public async Task EnqueueAsync_DuplicateItemId_Skipped()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        var itemId = item.ItemId;

        await _store.EnqueueAsync(item);
        await _store.EnqueueAsync(item); // duplicate

        Assert.Equal(1, await _store.GetPendingCountAsync());
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_ForExistingItem()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        await _store.EnqueueAsync(item);

        Assert.True(await _store.ExistsAsync(item.ItemId));
        Assert.False(await _store.ExistsAsync("nonexistent-id"));
    }

    #endregion

    #region Bounded queue — drop oldest synced

    [Fact]
    public async Task EnqueueAsync_WhenAtCap_PrunesOldestSynced()
    {
        // PruneSyncedKeep=0 forces prune to evict ALL synced rows when cap is hit.
        var smallConfig = new OfflineSyncConfig
        {
            MaxQueueSize = 3,
            DropOldestSyncedOnCap = true,
            PruneSyncedKeep = 0,
            PruneSyncedThreshold = 0
        };
        var loggerMock = new Mock<ILogger<SqliteOutboxStore>>();
        var store = new SqliteOutboxStore(_sessionStore, smallConfig, loggerMock.Object);

        // Fill with 3 items
        for (int i = 0; i < 3; i++)
        {
            await store.EnqueueAsync(OutboxItem.SensorLog($"s{i}", $"c{i}", new { n = i }));
        }

        // Mark first item as synced
        var firstItem = (await store.GetPendingAsync()).First();
        await store.MarkSyncedAsync(firstItem.ItemId);

        // Enqueue a 4th item — should trigger pruning of the synced row
        await store.EnqueueAsync(OutboxItem.SensorLog("s4", "c4", new { n = 4 }));

        // First item should have been pruned (synced items removed first)
        Assert.False(await store.ExistsAsync(firstItem.ItemId));
        Assert.Equal(3, await store.GetPendingCountAsync());
    }

    #endregion

    #region Mark synced / failed / retry

    [Fact]
    public async Task MarkSyncedAsync_KeepsRow_AndSetsStatus()
    {
        // task_073: MarkSyncedAsync must UPDATE status='Synced' (not DELETE),
        // so PruneSyncedToCapAsync / GetSyncedCountAsync can see the row.
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        await _store.EnqueueAsync(item);

        await _store.MarkSyncedAsync(item.ItemId);

        // Row is still present (status='Synced'), but is no longer pending.
        Assert.True(await _store.ExistsAsync(item.ItemId));
        Assert.Equal(0, await _store.GetPendingCountAsync());
        Assert.Equal(1, await _store.GetSyncedCountAsync());
    }

    [Fact]
    public async Task IncrementRetryAsync_IncrementsCount()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        await _store.EnqueueAsync(item);

        await _store.IncrementRetryAsync(item.ItemId, "network error");
        var after = await _store.GetPendingAsync();

        Assert.Single(after);
        Assert.Equal(1, after[0].RetryCount);
        Assert.Equal("network error", after[0].LastError);
    }

    [Fact]
    public async Task MarkFailedAsync_SetsStatusAndError()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        await _store.EnqueueAsync(item);

        await _store.MarkFailedAsync(item.ItemId, "permanent failure");

        Assert.Equal(0, await _store.GetPendingCountAsync());
        // Failed items are not in pending queue
    }

    #endregion

    #region TTL cleanup

    [Fact]
    public async Task DeleteOlderThanAsync_DoesNotDeletePending()
    {
        // task_073: TTL purge must not silently drop Pending items that the
        // agent is still trying to sync. Only Synced/Failed rows are eligible.
        var pending = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        await _store.EnqueueAsync(pending);

        // Cutoff in the future — would match the row by created_at, but the
        // status filter must protect Pending items.
        var cutoff = DateTime.UtcNow.AddYears(1);
        var deleted = await _store.DeleteOlderThanAsync(cutoff);

        Assert.Equal(0, deleted);
        Assert.True(await _store.ExistsAsync(pending.ItemId));
        Assert.Equal(1, await _store.GetPendingCountAsync());
    }

    [Fact]
    public async Task DeleteOlderThanAsync_DeletesSyncedRowsOlderThanCutoff()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        await _store.EnqueueAsync(item);
        await _store.MarkSyncedAsync(item.ItemId);

        // Cutoff in the future — the just-created Synced row is "older than"
        // the cutoff, so it must be deleted.
        var cutoff = DateTime.UtcNow.AddYears(1);
        var deleted = await _store.DeleteOlderThanAsync(cutoff);

        Assert.Equal(1, deleted);
        Assert.False(await _store.ExistsAsync(item.ItemId));
    }

    [Fact]
    public async Task DeleteOlderThanAsync_KeepsRowsNewerThanCutoff()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        await _store.EnqueueAsync(item);
        await _store.MarkSyncedAsync(item.ItemId);

        // Cutoff in the past — the just-created row is "newer than" the
        // cutoff, so it must remain.
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var deleted = await _store.DeleteOlderThanAsync(cutoff);

        Assert.Equal(0, deleted);
        Assert.True(await _store.ExistsAsync(item.ItemId));
    }

    #endregion

    #region Prune-on-enqueue (task_073)

    [Fact]
    public async Task EnqueueAsync_TriggersPruneWhenSyncedAboveThreshold()
    {
        // Below MaxQueueSize but above PruneSyncedThreshold: prune to keep cap.
        var cfg = new OfflineSyncConfig
        {
            MaxQueueSize = 100,
            PruneSyncedThreshold = 2,
            PruneSyncedKeep = 1
        };
        var loggerMock = new Mock<ILogger<SqliteOutboxStore>>();
        var store = new SqliteOutboxStore(_sessionStore, cfg, loggerMock.Object);

        // Enqueue 3 pending items
        var items = new List<OutboxItem>();
        for (int i = 0; i < 3; i++)
        {
            var it = OutboxItem.SensorLog($"s{i}", $"c{i}", new { n = i });
            items.Add(it);
            await store.EnqueueAsync(it);
        }

        // Mark all 3 as Synced → synced tail grows past threshold
        foreach (var it in items) await store.MarkSyncedAsync(it.ItemId);
        Assert.Equal(3, await store.GetSyncedCountAsync());

        // Enqueue one more — should trigger prune to PruneSyncedKeep=1
        await store.EnqueueAsync(OutboxItem.SensorLog("s4", "c4", new { n = 4 }));

        var synced = await store.GetSyncedCountAsync();
        Assert.True(synced <= cfg.PruneSyncedKeep,
            $"expected synced <= {cfg.PruneSyncedKeep}, got {synced}");
    }

    [Fact]
    public async Task PruneSyncedToCapAsync_EvictsOldestFirst()
    {
        // Synced rows have a synced_at timestamp; the oldest must be pruned first.
        var item1 = OutboxItem.SensorLog("s1", "c1", new { n = 1 });
        var item2 = OutboxItem.SensorLog("s2", "c2", new { n = 2 });
        var item3 = OutboxItem.SensorLog("s3", "c3", new { n = 3 });
        await _store.EnqueueAsync(item1);
        await _store.EnqueueAsync(item2);
        await _store.EnqueueAsync(item3);

        await _store.MarkSyncedAsync(item1.ItemId);
        await Task.Delay(10); // ensure synced_at ordering
        await _store.MarkSyncedAsync(item2.ItemId);
        await Task.Delay(10);
        await _store.MarkSyncedAsync(item3.ItemId);

        // Prune to keep only 1 → expect 2 deleted, only the newest (item3) remains.
        var pruned = await _store.PruneSyncedToCapAsync(1);

        Assert.Equal(2, pruned);
        Assert.False(await _store.ExistsAsync(item1.ItemId));
        Assert.False(await _store.ExistsAsync(item2.ItemId));
        Assert.True(await _store.ExistsAsync(item3.ItemId));
        Assert.Equal(1, await _store.GetSyncedCountAsync());
    }

    #endregion
}

public class OfflineSyncServiceTests
{
    private readonly Mock<IMeshBus> _busMock;
    private readonly Mock<INetworkMonitor> _networkMock;
    private readonly OfflineSyncConfig _config;
    private readonly MeshConfig _meshConfig;

    public OfflineSyncServiceTests()
    {
        _busMock = new Mock<IMeshBus>();
        _networkMock = new Mock<INetworkMonitor>();
        _config = new OfflineSyncConfig { Enabled = true, MaxRetries = 2 };
        _meshConfig = new MeshConfig { AgentId = "test-agent" };
    }

    [Fact]
    public async Task EnqueueAsync_WhenDisabled_SkipsEnqueue()
    {
        var disabledConfig = new OfflineSyncConfig { Enabled = false };
        var storeMock = new Mock<IOutboxStore>();
        var loggerMock = new Mock<ILogger<OfflineSyncService>>();
        var service = new OfflineSyncService(
            storeMock.Object, _busMock.Object, _networkMock.Object,
            disabledConfig, _meshConfig, loggerMock.Object!);

        await service.EnqueueAsync(OutboxItem.SensorLog("s1", "c1", new { x = 1 }));

        storeMock.Verify(s => s.EnqueueAsync(It.IsAny<OutboxItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FlushAsync_WhenDisabled_ReturnsZeroResult()
    {
        var disabledConfig = new OfflineSyncConfig { Enabled = false };
        var loggerMock = new Mock<ILogger<OfflineSyncService>>();
        var service = new OfflineSyncService(
            new Mock<IOutboxStore>().Object, _busMock.Object, _networkMock.Object,
            disabledConfig, _meshConfig, loggerMock.Object!);

        var result = await service.FlushAsync();

        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, result.SuccessCount);
    }

    [Fact]
    public async Task FlushAsync_EmptyQueue_ReturnsZeroResult()
    {
        var storeMock = new Mock<IOutboxStore>();
        storeMock.Setup(s => s.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboxItem>());
        var loggerMock = new Mock<ILogger<OfflineSyncService>>();
        var service = new OfflineSyncService(
            storeMock.Object, _busMock.Object, _networkMock.Object,
            _config, _meshConfig, loggerMock.Object!);

        var result = await service.FlushAsync();

        Assert.Equal(0, result.TotalProcessed);
        _busMock.Verify(b => b.PublishAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FlushAsync_PublishesItemsToMeshBus_AndMarksSynced()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { data = 42 });
        var storeMock = new Mock<IOutboxStore>();
        storeMock.Setup(s => s.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboxItem> { item });
        _busMock.Setup(b => b.PublishAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OfflineSyncService>>();
        var service = new OfflineSyncService(
            storeMock.Object, _busMock.Object, _networkMock.Object,
            _config, _meshConfig, loggerMock.Object!);

        var result = await service.FlushAsync();

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        _busMock.Verify(b => b.PublishAsync("offline/sensor-log", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
        storeMock.Verify(s => s.MarkSyncedAsync(item.ItemId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FlushAsync_OnPublishFailure_IncrementsRetryAndDoesNotMarkSynced()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        var storeMock = new Mock<IOutboxStore>();
        storeMock.Setup(s => s.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboxItem> { item });
        _busMock.Setup(b => b.PublishAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network down"));

        var loggerMock = new Mock<ILogger<OfflineSyncService>>();
        var service = new OfflineSyncService(
            storeMock.Object, _busMock.Object, _networkMock.Object,
            _config, _meshConfig, loggerMock.Object!);

        var result = await service.FlushAsync();

        Assert.Equal(1, result.FailureCount);
        Assert.Equal(0, result.SuccessCount);
        storeMock.Verify(s => s.IncrementRetryAsync(item.ItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        storeMock.Verify(s => s.MarkSyncedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FlushAsync_WhenMaxRetriesExceeded_MarksFailed()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        item.RetryCount = 2; // MaxRetries = 2

        var storeMock = new Mock<IOutboxStore>();
        storeMock.Setup(s => s.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboxItem> { item });

        var loggerMock = new Mock<ILogger<OfflineSyncService>>();
        var service = new OfflineSyncService(
            storeMock.Object, _busMock.Object, _networkMock.Object,
            _config, _meshConfig, loggerMock.Object!);

        var result = await service.FlushAsync();

        Assert.Equal(1, result.FailureCount);
        storeMock.Verify(s => s.MarkFailedAsync(item.ItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _busMock.Verify(b => b.PublishAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FlushAsync_ThreeItemTypes_PublishToCorrectTopics()
    {
        var items = new List<OutboxItem>
        {
            OutboxItem.SensorLog("s1", "c1", new { x = 1 }),
            OutboxItem.TaskResult("s1", "c1", new { x = 2 }),
            OutboxItem.Alert("s1", "c1", new { x = 3 }),
        };
        var storeMock = new Mock<IOutboxStore>();
        storeMock.Setup(s => s.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        _busMock.Setup(b => b.PublishAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OfflineSyncService>>();
        var service = new OfflineSyncService(
            storeMock.Object, _busMock.Object, _networkMock.Object,
            _config, _meshConfig, loggerMock.Object!);

        await service.FlushAsync();

        _busMock.Verify(b => b.PublishAsync("offline/sensor-log", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
        _busMock.Verify(b => b.PublishAsync("offline/task-result", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
        _busMock.Verify(b => b.PublishAsync("offline/alert", It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FlushAsync_UsesCorrectSenderAgentId()
    {
        var item = OutboxItem.SensorLog("s1", "c1", new { x = 1 });
        IntentEnvelope? capturedEnvelope = null;
        _busMock.Setup(b => b.PublishAsync(It.IsAny<string>(), It.IsAny<IntentEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<string, IntentEnvelope, CancellationToken>((_, e, _) => capturedEnvelope = e)
            .Returns(Task.CompletedTask);

        var storeMock = new Mock<IOutboxStore>();
        storeMock.Setup(s => s.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboxItem> { item });

        var loggerMock = new Mock<ILogger<OfflineSyncService>>();
        var meshCfg = new MeshConfig { AgentId = "my-fleet-agent-42" };
        var service = new OfflineSyncService(
            storeMock.Object, _busMock.Object, _networkMock.Object,
            _config, meshCfg, loggerMock.Object);

        await service.FlushAsync();

        Assert.NotNull(capturedEnvelope);
        Assert.Equal("my-fleet-agent-42", capturedEnvelope.Sender);
    }
}
