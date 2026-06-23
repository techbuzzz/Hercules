using HerculesBus;
using HerculesBus.Core;
using HerculesBus.InMemory;
using HerculesBus.Sqlite;
using Xunit;

namespace Hercules.Agent.Tests.BusTests;

/// <summary>
///     Тесты для SQLite-backed реализаций IChannelStore и IAgentRegistry.
///     Каждый тест использует временный файл БД в %TEMP%, удаляется в конце.
/// </summary>
public class SqliteChannelStoreTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SqliteChannelStore _store;
    private readonly SqliteAgentRegistry _registry;
    private readonly Bus _bus;

    public SqliteChannelStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hercules-bus-test-{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={_dbPath}";
        _store = new SqliteChannelStore(connStr);
        _registry = new SqliteAgentRegistry(connStr);
        _bus = new Bus(_store, _registry, new InMemoryEventBus());
    }

    public async ValueTask DisposeAsync()
    {
        await _store.DisposeAsync();
        await _registry.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task EnsureChannel_Persists_Across_New_Store_Instance()
    {
        await _store.EnsureChannelAsync("main", "Main", isPrivate: false, createdBy: "alice");

        // Новый instance с тем же файлом → канал должен быть виден
        await using var store2 = new SqliteChannelStore($"Data Source={_dbPath}");
        var ch = await store2.GetChannelAsync("main");
        Assert.NotNull(ch);
        Assert.Equal("Main", ch!.Description);
        Assert.Equal("alice", ch.CreatedBy);
    }

    [Fact]
    public async Task AppendMessage_Persists_With_Server_Side_Id()
    {
        await _store.EnsureChannelAsync("test", "", false, "alice");

        var sent = await _store.AppendMessageAsync(new AgentMessage(
            Id: "", Channel: "test",
            SenderAgentId: "alice", SenderName: "Alice",
            Kind: MessageKinds.Text, Body: "hello"));

        Assert.Equal(26, sent.Id.Length);

        // Перезагрузка
        await using var store2 = new SqliteChannelStore($"Data Source={_dbPath}");
        var got = await store2.GetMessageAsync(sent.Id);
        Assert.NotNull(got);
        Assert.Equal("hello", got!.Body);
    }

    [Fact]
    public async Task GetRecentMessages_Returns_Ordered_By_Timestamp_Ascending()
    {
        await _store.EnsureChannelAsync("test", "", false, "alice");

        for (int i = 0; i < 5; i++)
        {
            await _store.AppendMessageAsync(new AgentMessage(
                Id: "", Channel: "test",
                SenderAgentId: "alice", SenderName: "Alice",
                Kind: MessageKinds.Text, Body: $"msg-{i}",
                Timestamp: DateTimeOffset.UtcNow.AddMilliseconds(i * 10)));
        }

        var recent = await _store.GetRecentMessagesAsync("test", limit: 10);
        Assert.Equal(5, recent.Count);
        Assert.Equal("msg-0", recent[0].Body);
        Assert.Equal("msg-4", recent[4].Body);
    }

    [Fact]
    public async Task GetThread_Returns_Only_Replies_To_Parent()
    {
        await _store.EnsureChannelAsync("test", "", false, "alice");
        var parent = await _store.AppendMessageAsync(new AgentMessage(
            Id: "", Channel: "test",
            SenderAgentId: "alice", SenderName: "Alice",
            Kind: MessageKinds.Text, Body: "parent"));

        await _store.AppendMessageAsync(new AgentMessage("", "test", "bob", "Bob", MessageKinds.Text, "unrelated"));
        await _store.AppendMessageAsync(new AgentMessage("", "test", "bob", "Bob", MessageKinds.Text, "reply-1", ReplyTo: parent.Id));
        await _store.AppendMessageAsync(new AgentMessage("", "test", "carol", "Carol", MessageKinds.Text, "reply-2", ReplyTo: parent.Id));

        var thread = await _store.GetThreadAsync(parent.Id);
        Assert.Equal(2, thread.Count);
        Assert.All(thread, m => Assert.Equal(parent.Id, m.ReplyTo));
        Assert.Equal("reply-1", thread[0].Body);
        Assert.Equal("reply-2", thread[1].Body);
    }

    [Fact]
    public async Task Registry_Register_Persists_Across_New_Instance()
    {
        await _registry.RegisterAsync(new AgentIdentity("alice", "Alice", new[] { "hercules-agent" }, "tok"));

        await using var reg2 = new SqliteAgentRegistry($"Data Source={_dbPath}");
        var info = await reg2.GetAsync("alice");
        Assert.NotNull(info);
        Assert.Equal("Alice", info!.DisplayName);
    }

    [Fact]
    public async Task Heartbeat_Updates_LastSeen_In_Sqlite()
    {
        await _registry.RegisterAsync(new AgentIdentity("alice", "Alice", new[] { "hercules-agent" }, "tok"));
        var before = await _registry.GetAsync("alice");
        await Task.Delay(50);
        await _registry.HeartbeatAsync("alice", AgentStatus.Busy);

        var after = await _registry.GetAsync("alice");
        Assert.Equal(AgentStatus.Busy, after!.Status);
        Assert.True(after.LastSeen > before!.LastSeen);
    }

    [Fact]
    public async Task Bus_With_Sqlite_Stores_Messages_Persistently()
    {
        await _bus.EnsureChannelAsync("main", "", false, "alice");
        await _bus.RegisterAsync(new AgentIdentity("alice", "Alice", new[] { "hercules-agent" }, "tok"));

        await _bus.SendAsync(new AgentMessage("", "main", "alice", "Alice", MessageKinds.Text, "msg-1"));
        await _bus.SendAsync(new AgentMessage("", "main", "alice", "Alice", MessageKinds.Text, "msg-2"));

        // Новый Bus с тем же SQLite — сообщения должны быть
        await using var store2 = new SqliteChannelStore($"Data Source={_dbPath}");
        await using var reg2 = new SqliteAgentRegistry($"Data Source={_dbPath}");
        var bus2 = new Bus(store2, reg2, new InMemoryEventBus());

        var recent = await bus2.GetRecentAsync("main");
        Assert.Equal(2, recent.Count);
        Assert.Equal("msg-1", recent[0].Body);
        Assert.Equal("msg-2", recent[1].Body);
    }
}
