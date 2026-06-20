using HerculesBus;
using HerculesBus.InMemory;
using Xunit;

namespace Hercules.Agent.Tests.Bus;

public class HerculesBusTests
{
    private static global::HerculesBus.HerculesBus CreateBus() => new(
        new InMemoryChannelStore(),
        new InMemoryAgentRegistry(),
        new InMemoryEventBus());

    [Fact]
    public async Task Register_Agent_Adds_To_Registry()
    {
        var bus = CreateBus();
        var identity = new AgentIdentity("test-agent", "Test Agent", new[] { "hercules-agent" }, "tok-1");

        var result = await bus.RegisterAsync(identity);

        Assert.True(result.IsNew);
        Assert.Equal("test-agent", result.Info.AgentId);
        Assert.Equal(AgentStatus.Online, result.Info.Status);
    }

    [Fact]
    public async Task EnsureChannel_Creates_And_Dedups()
    {
        var bus = CreateBus();

        var ch1 = await bus.EnsureChannelAsync("main", "Main", isPrivate: false, createdBy: "alice");
        var ch2 = await bus.EnsureChannelAsync("main", "duplicate", isPrivate: true, createdBy: "bob");

        // Должен вернуть тот же канал (first-write-wins)
        Assert.Equal(ch1.Name, ch2.Name);
        Assert.Equal(ch1.CreatedAt, ch2.CreatedAt);
        Assert.Equal(ch1.CreatedBy, ch2.CreatedBy);
        Assert.False(ch2.IsPrivate); // флаг не должен перезаписываться
    }

    [Fact]
    public async Task SendAsync_Stores_And_Publishes_Message()
    {
        var bus = CreateBus();
        await bus.EnsureChannelAsync("test", "", false, "alice");

        var received = new List<AgentMessage>();
        await using var sub = bus.Subscribe("test", async (msg, _) =>
        {
            received.Add(msg);
            await ValueTask.CompletedTask;
        });

        var sent = await bus.SendAsync(new AgentMessage(
            Id: "",
            Channel: "test",
            SenderAgentId: "alice",
            SenderName: "Alice",
            Kind: MessageKinds.Text,
            Body: "hello world"));

        // Подписчик получил
        await Task.Delay(50);
        Assert.Single(received);
        Assert.Equal(sent.Id, received[0].Id);

        // История сохранилась
        var recent = await bus.GetRecentAsync("test");
        Assert.Single(recent);
        Assert.Equal("hello world", recent[0].Body);
    }

    [Fact]
    public async Task SendAsync_Generates_Server_Side_Id_And_Timestamp()
    {
        var bus = CreateBus();
        await bus.EnsureChannelAsync("test", "", false, "alice");

        var sent = await bus.SendAsync(new AgentMessage(
            Id: "",
            Channel: "test",
            SenderAgentId: "alice",
            SenderName: "Alice",
            Kind: MessageKinds.Text,
            Body: "msg"));

        Assert.Equal(26, sent.Id.Length);
        Assert.NotNull(sent.Timestamp);
    }

    [Fact]
    public async Task SendAsync_Preserves_Client_Side_Id_If_Provided()
    {
        var bus = CreateBus();
        await bus.EnsureChannelAsync("test", "", false, "alice");

        var sent = await bus.SendAsync(new AgentMessage(
            Id: "custom-id-123",
            Channel: "test",
            SenderAgentId: "alice",
            SenderName: "Alice",
            Kind: MessageKinds.Text,
            Body: "msg"));

        Assert.Equal("custom-id-123", sent.Id);
    }

    [Fact]
    public async Task Multiple_Subscribers_All_Receive_Broadcast()
    {
        var bus = CreateBus();
        await bus.EnsureChannelAsync("test", "", false, "alice");

        var received1 = new List<AgentMessage>();
        var received2 = new List<AgentMessage>();

        await using var s1 = bus.Subscribe("test", async (m, _) => { lock (received1) received1.Add(m); await ValueTask.CompletedTask; });
        await using var s2 = bus.Subscribe("test", async (m, _) => { lock (received2) received2.Add(m); await ValueTask.CompletedTask; });

        await bus.SendAsync(new AgentMessage("", "test", "alice", "Alice", MessageKinds.Text, "hi"));
        await Task.Delay(50);

        Assert.Single(received1);
        Assert.Single(received2);
    }

    [Fact]
    public async Task Subscribe_Does_Not_Receive_Other_Channels()
    {
        var bus = CreateBus();
        await bus.EnsureChannelAsync("a", "", false, "alice");
        await bus.EnsureChannelAsync("b", "", false, "alice");

        var received = new List<AgentMessage>();
        await using var sub = bus.Subscribe("a", async (m, _) => { received.Add(m); await ValueTask.CompletedTask; });

        await bus.SendAsync(new AgentMessage("", "b", "alice", "Alice", MessageKinds.Text, "b-msg"));
        await bus.SendAsync(new AgentMessage("", "a", "alice", "Alice", MessageKinds.Text, "a-msg"));
        await Task.Delay(50);

        Assert.Single(received);
        Assert.Equal("a-msg", received[0].Body);
    }

    [Fact]
    public async Task SubscribeAll_Receives_From_All_Channels()
    {
        var bus = CreateBus();
        await bus.EnsureChannelAsync("a", "", false, "alice");
        await bus.EnsureChannelAsync("b", "", false, "alice");

        var received = new List<AgentMessage>();
        await using var sub = bus.SubscribeAll(async (m, _) => { received.Add(m); await ValueTask.CompletedTask; });

        await bus.SendAsync(new AgentMessage("", "a", "alice", "Alice", MessageKinds.Text, "a-msg"));
        await bus.SendAsync(new AgentMessage("", "b", "bob", "Bob", MessageKinds.Text, "b-msg"));
        await Task.Delay(50);

        Assert.Equal(2, received.Count);
    }

    [Fact]
    public async Task ReplyAsync_Stores_With_ReplyTo()
    {
        var bus = CreateBus();
        await bus.EnsureChannelAsync("test", "", false, "alice");

        var parent = await bus.SendAsync(new AgentMessage("", "test", "alice", "Alice", MessageKinds.Text, "parent"));
        var reply = await bus.ReplyAsync(parent.Id, new AgentMessage("", "test", "bob", "Bob", MessageKinds.Text, "reply"));

        Assert.Equal(parent.Id, reply.ReplyTo);

        var thread = await bus.GetThreadAsync(parent.Id);
        Assert.Single(thread);
        Assert.Equal("reply", thread[0].Body);
    }

    [Fact]
    public async Task GetRecentAsync_Returns_Ordered_By_Time()
    {
        var bus = CreateBus();
        await bus.EnsureChannelAsync("test", "", false, "alice");

        for (int i = 0; i < 5; i++)
        {
            await bus.SendAsync(new AgentMessage("", "test", "alice", "Alice", MessageKinds.Text, $"msg-{i}"));
            await Task.Delay(5);
        }

        var recent = await bus.GetRecentAsync("test", limit: 3);
        Assert.Equal(3, recent.Count);
        Assert.Equal("msg-2", recent[0].Body);
        Assert.Equal("msg-3", recent[1].Body);
        Assert.Equal("msg-4", recent[2].Body);
    }

    [Fact]
    public async Task Heartbeat_Updates_LastSeen_And_Status()
    {
        var bus = CreateBus();
        var identity = new AgentIdentity("test-agent", "Test", new[] { "hercules-agent" }, "tok");
        await bus.RegisterAsync(identity);

        var before = await bus.GetAgentAsync("test-agent");
        Assert.Equal(AgentStatus.Online, before!.Status);

        await Task.Delay(50);
        await bus.HeartbeatAsync("test-agent", AgentStatus.Busy);

        var after = await bus.GetAgentAsync("test-agent");
        Assert.Equal(AgentStatus.Busy, after!.Status);
        Assert.True(after.LastSeen > before.LastSeen);
    }

    [Fact]
    public async Task ListAgents_Filters_Offline()
    {
        var bus = CreateBus();
        await bus.RegisterAsync(new AgentIdentity("a", "A", new[] { "hercules-agent" }, "tok"));
        await bus.RegisterAsync(new AgentIdentity("b", "B", new[] { "hercules-agent" }, "tok"));

        var all = await bus.ListAgentsAsync(includeOffline: true);
        Assert.Equal(2, all.Count);

        var onlineOnly = await bus.ListAgentsAsync(includeOffline: false);
        Assert.Equal(2, onlineOnly.Count); // оба только что зарегистрированы
    }

    [Fact]
    public async Task SubscribeAsync_Returns_Stream_Of_Messages()
    {
        var bus = CreateBus();
        await bus.EnsureChannelAsync("test", "", false, "alice");

        var received = new List<AgentMessage>();
        var consumeTask = Task.Run(async () =>
        {
            await foreach (var msg in bus.SubscribeAsync("test", CancellationToken.None))
            {
                received.Add(msg);
                if (received.Count >= 2) break;
            }
        });

        // Дать подписчику зарегистрироваться
        await Task.Delay(50);

        await bus.SendAsync(new AgentMessage("", "test", "alice", "Alice", MessageKinds.Text, "m1"));
        await bus.SendAsync(new AgentMessage("", "test", "alice", "Alice", MessageKinds.Text, "m2"));

        await consumeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, received.Count);
        Assert.Equal("m1", received[0].Body);
        Assert.Equal("m2", received[1].Body);
    }
}
