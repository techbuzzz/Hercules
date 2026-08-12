using System.Text.Json;
using Hercules.Mcp;
using Hercules.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Mcp;

/// <summary>
///     Tests for <see cref="McpToolAdapter"/>.
/// </summary>
public class McpToolAdapterTests
{
    // --- Name format ---

    [Fact]
    public void Name_ScopesToolToServer()
    {
        var tool = new FakeMcpClientTool("echo", "Echoes input");
        var adapter = new McpToolAdapter(tool, "myserver", NoOpLogger());

        Assert.Equal("mcp.myserver.echo", adapter.Name);
    }

    [Fact]
    public void Name_IncludesServerNameInPath()
    {
        var tool = new FakeMcpClientTool("search", "Search the web");
        var adapter = new McpToolAdapter(tool, "filesystem", NoOpLogger());

        Assert.Equal("mcp.filesystem.search", adapter.Name);
    }

    [Fact]
    public void Name_ServerNameIsCasePreserved()
    {
        var tool = new FakeMcpClientTool("run", "Run a command");
        var adapter = new McpToolAdapter(tool, "SomeServer", NoOpLogger());

        Assert.Equal("mcp.SomeServer.run", adapter.Name);
    }

    // --- Description passthrough ---

    [Fact]
    public void Description_ReturnsToolDescription()
    {
        var tool = new FakeMcpClientTool("greet", "Greets the user by name");
        var adapter = new McpToolAdapter(tool, "server1", NoOpLogger());

        Assert.Equal("Greets the user by name", adapter.Description);
    }

    [Fact]
    public void Description_FallsBackToNameWhenNull()
    {
        var tool = new FakeMcpClientTool("cmd", null);
        var adapter = new McpToolAdapter(tool, "server1", NoOpLogger());

        Assert.Equal("cmd", adapter.Description);
    }

    // --- ParametersSchema ---

    [Fact]
    public void ParametersSchema_ReturnsNull_WhenSchemaIsUndefined()
    {
        var tool = new FakeMcpClientTool("simple", "A simple tool") { JsonSchema = default };
        var adapter = new McpToolAdapter(tool, "server1", NoOpLogger());

        Assert.Null(adapter.ParametersSchema);
    }

    [Fact]
    public void ParametersSchema_ReturnsRawJson()
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(
            """{"type":"object","properties":{"message":{"type":"string"}}}""");
        var tool = new FakeMcpClientTool("echo", "Echoes back") { JsonSchema = schema };
        var adapter = new McpToolAdapter(tool, "server1", NoOpLogger());

        var raw = adapter.ParametersSchema;
        Assert.NotNull(raw);
        Assert.Contains("\"message\"", raw);
    }

    [Fact]
    public void ParametersSchema_ReturnsRawJson_EvenForNonObjectTypes()
    {
        // An array is valid JSON; the adapter just passes GetRawText() through without validation.
        var schema = JsonSerializer.Deserialize<JsonElement>("[1,2,3]");
        var tool = new FakeMcpClientTool("array", "Array schema tool") { JsonSchema = schema };
        var adapter = new McpToolAdapter(tool, "server1", NoOpLogger());

        var raw = adapter.ParametersSchema;
        Assert.NotNull(raw);
        Assert.Contains("[1,2,3]", raw);
    }

    // --- ExecuteAsync: success path ---

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsOkOutput()
    {
        var tool = new FakeMcpClientTool("add", "Adds numbers")
        {
            OnCall = _ => new ModelContextProtocol.Protocol.CallToolResult
            {
                Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = "42" }]
            }
        };
        var adapter = new McpToolAdapter(tool, "calc", NoOpLogger());

        var result = await adapter.ExecuteAsync("""{"a":1,"b":2}""");

        Assert.True(result.Success);
        Assert.Contains("42", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_PassesArgumentsToTool()
    {
        int? capturedCount = null;
        var tool = new FakeMcpClientTool("search", "Search")
        {
            OnCall = args =>
            {
                capturedCount = args?.Count ?? 0;
                return new ModelContextProtocol.Protocol.CallToolResult
                {
                    Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = "ok" }]
                };
            }
        };
        var adapter = new McpToolAdapter(tool, "search", NoOpLogger());

        await adapter.ExecuteAsync("""{"query":"hello"}""");

        Assert.Equal(1, capturedCount);
    }

    [Fact]
    public async Task ExecuteAsync_ToolReturnsError_SetsSuccessFalse()
    {
        var tool = new FakeMcpClientTool("fail", "Always fails")
        {
            OnCall = _ => new ModelContextProtocol.Protocol.CallToolResult
            {
                IsError = true,
                Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = "something went wrong" }]
            }
        };
        var adapter = new McpToolAdapter(tool, "server", NoOpLogger());

        var result = await adapter.ExecuteAsync("{}");

        Assert.False(result.Success);
        Assert.Contains("something went wrong", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsFail()
    {
        var tool = new FakeMcpClientTool("any", "Any tool");
        var adapter = new McpToolAdapter(tool, "server", NoOpLogger());

        var result = await adapter.ExecuteAsync("not valid json");

        Assert.False(result.Success);
        Assert.Contains("Invalid arguments JSON", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyArgs_PassesNullArguments()
    {
        bool receivedNull = false;
        var tool = new FakeMcpClientTool("ping", "Ping")
        {
            OnCall = args =>
            {
                receivedNull = args == null;
                return new ModelContextProtocol.Protocol.CallToolResult
                {
                    Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = "pong" }]
                };
            }
        };
        var adapter = new McpToolAdapter(tool, "server", NoOpLogger());

        await adapter.ExecuteAsync("");

        Assert.True(receivedNull);
    }

    [Fact]
    public async Task ExecuteAsync_NullContent_ReturnsNoOutput()
    {
        var tool = new FakeMcpClientTool("empty", "Empty result")
        {
            OnCall = _ => new ModelContextProtocol.Protocol.CallToolResult
            {
                Content = Array.Empty<ModelContextProtocol.Protocol.ContentBlock>()
            }
        };
        var adapter = new McpToolAdapter(tool, "server", NoOpLogger());

        var result = await adapter.ExecuteAsync("{}");

        Assert.True(result.Success);
        Assert.Equal("(no output)", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_ReturnsCancelled()
    {
        var tool = new FakeMcpClientTool("slow", "Slow tool");
        var adapter = new McpToolAdapter(tool, "server", NoOpLogger());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await adapter.ExecuteAsync("{}", cts.Token);

        Assert.False(result.Success);
        Assert.Contains("cancelled", result.Output);
    }

    [Fact]
    public void Constructor_NullTool_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new McpToolAdapter((IMcpClientTool)null!, "server", NoOpLogger()));
    }

    [Fact]
    public void Constructor_NullServerName_Throws()
    {
        var tool = new FakeMcpClientTool("x", "y");
        Assert.Throws<ArgumentNullException>(() =>
            new McpToolAdapter(tool, null!, NoOpLogger()));
    }

    private static ILogger<McpToolAdapter> NoOpLogger()
        => new Mock<ILogger<McpToolAdapter>>().Object;
}

/// <summary>
///     Fake <see cref="IMcpClientTool"/> for testing.
///     Allows configuring <see cref="OnCall"/> with a simple <c>Func&lt;args, CallToolResult&gt;</c>
///     (cancellation token and ValueTask wrapper are handled internally).
/// </summary>
internal sealed class FakeMcpClientTool : IMcpClientTool
{
    /// <summary>
    ///     Configures the tool call result. Set to a lambda that receives the deserialized
    ///     arguments dictionary and returns a <see cref="ModelContextProtocol.Protocol.CallToolResult"/>.
    /// </summary>
    public Func<IReadOnlyDictionary<string, object?>?, ModelContextProtocol.Protocol.CallToolResult> OnCall { private get; set; }
        = _ => new ModelContextProtocol.Protocol.CallToolResult
        {
            Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = "default" }]
        };

    public FakeMcpClientTool(string name, string? description)
    {
        Name = name;
        Description = description ?? name;
        JsonSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object"}""");
    }

    public string Name { get; }
    public string? Description { get; }
    public JsonElement JsonSchema { get; set; }

    public async ValueTask<ModelContextProtocol.Protocol.CallToolResult> CallAsync(
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException();

        // Offload to thread pool so async/await is properly used and the ct is honoured
        // when the wrapped function is synchronously fast.
        return await Task.Run(() => OnCall(arguments), cancellationToken);
    }
}
