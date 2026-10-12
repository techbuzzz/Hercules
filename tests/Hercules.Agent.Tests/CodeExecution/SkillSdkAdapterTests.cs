using Hercules.Agent;
using Hercules.CodeExecution;
using Hercules.CodeExecution.SkillSdkAdapters;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Memory.Layers;
using Hercules.SkillSdk;
using Hercules.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.CodeExecutionTests;

public sealed class SkillSdkAdapterTests
{
    private static ILogger Logger => NullLogger.Instance;

    [Theory]
    [InlineData("example.com", "example.com", true)]
    [InlineData("example.com", "evil.com", false)]
    [InlineData("*.example.com", "api.example.com", true)]
    [InlineData("*.example.com", "api.evil.com", false)]
    [InlineData("*", "anything.com", true)]
    public void HttpClientAdapter_Enforces_AllowedDomains(string pattern, string host, bool expected)
    {
        var cfg = new HttpConfig { AllowedDomains = new List<string> { pattern } };
        var actual = HttpClientAdapter.IsDomainAllowed(host, cfg);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task MemoryClientAdapter_Scopes_Execution_Memory()
    {
        var sessionId = "s1";
        var layered = CreateLayeredMemory();
        var facts = new Mock<IDurableFactsStore>().Object;
        var adapter = new MemoryClientAdapter(sessionId, layered, facts, Logger);

        await adapter.SetAsync("key", "execution-value", SkillMemoryScope.Execution);

        var value = await adapter.GetAsync("key", SkillMemoryScope.Execution);
        Assert.Equal("execution-value", value);

        // Execution memory must NOT leak into session scope.
        var sessionValue = await adapter.GetAsync("key", SkillMemoryScope.Session);
        Assert.Null(sessionValue);
    }

    [Fact]
    public async Task MemoryClientAdapter_Writes_Durable_Via_FactsStore()
    {
        var sessionId = "s1";
        var layered = CreateLayeredMemory();
        var factsMock = new Mock<IDurableFactsStore>();
        string? capturedKey = null;
        string? capturedValue = null;
        factsMock
            .Setup(f => f.StoreFactAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MemoryEntry>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, MemoryEntry, CancellationToken>((k, v, _, _) =>
            {
                capturedKey = k;
                capturedValue = v;
            })
            .Returns(Task.CompletedTask);

        var adapter = new MemoryClientAdapter(sessionId, layered, factsMock.Object, Logger);

        await adapter.SetAsync("fact", "durable-value", SkillMemoryScope.Durable);

        Assert.Equal("fact", capturedKey);
        Assert.Equal("durable-value", capturedValue);
    }

    [Fact]
    public void SessionContext_Exposes_SessionId_UserId_Metadata()
    {
        var metadata = new Dictionary<string, string> { ["agent"] = "hercules" };
        var session = new SessionContext("session-42", "user-7", metadata, CancellationToken.None);

        Assert.Equal("session-42", session.SessionId);
        Assert.Equal("user-7", session.UserId);
        Assert.Equal("hercules", session.Metadata["agent"]);
    }

    [Fact]
    public void HerculesSkillContext_GetConfig_Exposes_Only_Safe_Keys()
    {
        var cfg = new HttpConfig { TimeoutSeconds = 17 };
        var ctx = new HerculesSkillContext(
            "session",
            null,
            cfg,
            Mock.Of<ILLMClient>(),
            new ToolRegistry(Array.Empty<ITool>(), NullLoggerFactory.Instance),
            CreateLayeredMemory(),
            Mock.Of<IDurableFactsStore>(),
            Logger);

        Assert.Equal("17", ctx.GetConfig("Http.TimeoutSeconds"));
        Assert.Null(ctx.GetConfig("Http.ApiKey"));
        Assert.Null(ctx.GetConfig("Llm.YandexGpt.ApiKey"));
    }

    private static LayeredMemoryManager CreateLayeredMemory()
    {
        var working = new WorkingMemoryService();
        var facts = Mock.Of<IDurableFactsStore>();
        var episodes = Mock.Of<IEpisodicStore>();
        return new LayeredMemoryManager(working, facts, episodes);
    }
}
