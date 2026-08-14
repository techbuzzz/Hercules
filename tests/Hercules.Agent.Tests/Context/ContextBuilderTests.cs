using Hercules.Config;
using Hercules.Context;
using Hercules.Context.Summarizer;
using Hercules.Memory.Layers;
using Hercules.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Context;

/// <summary>
///     Тесты ContextBuilder (task_027).
/// </summary>
public class ContextBuilderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageConfig _storageConfig;
    private readonly LayeredMemoryManager _memoryManager;
    private readonly Mock<ILogger<ContextBuilder>> _loggerMock;
    private readonly ContextConfig _cfg;

    public ContextBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules_ctx_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _storageConfig = new StorageConfig
        {
            DataRoot = _tempDir,
            MemoryDir = "Memory"
        };

        var factsStore = new DurableFactsService(_storageConfig);
        var episodicStore = new EpisodicStore(_storageConfig);
        var workingMemory = new WorkingMemoryService(new LayeredMemoryConfig { MaxWorkingMemoryEntries = 100 });
        _memoryManager = new LayeredMemoryManager(workingMemory, factsStore, episodicStore);

        _loggerMock = new Mock<ILogger<ContextBuilder>>();
        _cfg = new ContextConfig
        {
            Enabled = true,
            MaxContextTokens = 6000,
            MaxFactsInContext = 20,
            MaxEpisodesInContext = 5,
            CompressionThreshold = 3
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task BuildContextAsync_WhenDisabled_ReturnsEmptyAssembly()
    {
        var disabledCfg = new ContextConfig { Enabled = false };
        var ctx = new ContextBuilder(null, disabledCfg, _loggerMock.Object);

        var result = await ctx.BuildContextAsync("test input", "sess1", null);

        Assert.Equal("", result.ContextBlock);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task BuildContextAsync_EmptyMemory_ReturnsEmptyContext()
    {
        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);

        var result = await ctx.BuildContextAsync("test input", "sess1", null);

        Assert.Equal("", result.ContextBlock.Trim());
    }

    [Fact]
    public async Task BuildContextAsync_WithFacts_ReturnsContextWithFacts()
    {
        // Add a fact
        await _memoryManager.StoreFactAsync("test_key", "test value",
            new MemoryEntry("test", MemoryConfidence.High));

        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);

        var result = await ctx.BuildContextAsync("test input", "sess1", null);

        Assert.Contains("test_key", result.ContextBlock);
        Assert.Contains("test value", result.ContextBlock);
        Assert.True(result.ItemCount > 0);
    }

    [Fact]
    public async Task BuildContextAsync_RespectsMaxFactsLimit()
    {
        var smallCfg = new ContextConfig
        {
            Enabled = true,
            MaxContextTokens = 6000,
            MaxFactsInContext = 1,
            CompressionThreshold = 3
        };

        // Add 5 facts
        for (int i = 0; i < 5; i++)
        {
            await _memoryManager.StoreFactAsync($"fact_{i}", $"value_{i}",
                new MemoryEntry("test", MemoryConfidence.High));
        }

        var ctx = new ContextBuilder(_memoryManager, smallCfg, _loggerMock.Object);

        var result = await ctx.BuildContextAsync("test", "sess1", null);

        // Only one fact should be in context
        Assert.True(result.Truncated);
    }

    [Fact]
    public void EstimateTokens_EmptyString_ReturnsZero()
    {
        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);

        var result = ctx.EstimateTokens("");

        Assert.Equal(0, result);
    }

    [Fact]
    public void EstimateTokens_NormalText_ReturnsLenDividedBy4()
    {
        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);

        var result = ctx.EstimateTokens("hello world");

        Assert.Equal(2, result); // 11 chars / 4 = 2.75 → int division = 2
    }

    [Fact]
    public void EstimateTokens_Exactly4Chars_ReturnsOne()
    {
        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);

        var result = ctx.EstimateTokens("test");

        Assert.Equal(1, result); // 4 / 4 = 1
    }

    [Fact]
    public void GetCurrentBudget_ReturnsConfiguredBudget()
    {
        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);

        var budget = ctx.GetCurrentBudget();

        Assert.Equal(_cfg.MaxContextTokens, budget.MaxTokens);
    }

    [Fact]
    public async Task CompressTrace_BelowThreshold_ReturnsFalse()
    {
        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);
        var trace = new List<ToolTraceEntry>
        {
            new("tool_a", "{}", "result", 100, DateTime.UtcNow, true),
        };

        var result = await ctx.CompressTraceAsync(trace, "sess1");

        Assert.False(result);
    }

    [Fact]
    public async Task CompressTrace_AtThreshold_ReturnsTrue()
    {
        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);
        var now = DateTime.UtcNow;
        var trace = new List<ToolTraceEntry>
        {
            new("tool_a", "{}", "r1", 10, now, true),
            new("tool_b", "{}", "r2", 10, now, true),
            new("tool_c", "{}", "r3", 10, now, true),
        };

        var result = await ctx.CompressTraceAsync(trace, "sess1");

        Assert.True(result);
    }

    [Fact]
    public async Task CompressTrace_AboveThreshold_ReturnsTrue()
    {
        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);
        var now = DateTime.UtcNow;
        var trace = new List<ToolTraceEntry>
        {
            new("tool_a", "{}", "r1", 10, now, true),
            new("tool_b", "{}", "r2", 10, now, true),
            new("tool_c", "{}", "r3", 10, now, true),
            new("tool_d", "{}", "r4", 10, now, true),
            new("tool_e", "{}", "r5", 10, now, true),
        };

        var result = await ctx.CompressTraceAsync(trace, "sess1");

        Assert.True(result);
    }

    [Fact]
    public async Task BuildContextAsync_WithWorkingMemory_IncludesWorkingMemory()
    {
        _memoryManager.SetWorking("scratch_key", "scratch value",
            new MemoryEntry("test", MemoryConfidence.Medium));

        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);

        var result = await ctx.BuildContextAsync("test input", "sess1", null);

        Assert.Contains("scratch_key", result.ContextBlock);
        Assert.Contains("scratch value", result.ContextBlock);
    }

    [Fact]
    public async Task BuildContextAsync_SensitiveFact_Excluded()
    {
        // Add a sensitive fact
        await _memoryManager.StoreFactAsync("secret_key", "secret value",
            new MemoryEntry("test", MemoryConfidence.High)
            {
                Sensitivity = MemorySensitivity.Sensitive,
                Tags = new List<string> { "sensitive" }
            });

        var ctx = new ContextBuilder(_memoryManager, _cfg, _loggerMock.Object);

        var result = await ctx.BuildContextAsync("test", "sess1", null);

        Assert.DoesNotContain("secret_key", result.ContextBlock);
    }
}
