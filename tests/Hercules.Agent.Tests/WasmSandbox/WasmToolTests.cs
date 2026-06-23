using Hercules.WasmSandbox;
using Hercules.WasmSandbox.Compilation;
using Xunit;

namespace Hercules.Agent.Tests.WasmSandbox;

/// <summary>
///     Тесты для высокоуровневого WasmTool: компиляция + sandbox execution + approval gate + cache.
/// </summary>
public class WasmToolTests
{
    [Fact]
    public async Task Passthrough_Compiler_Accepts_Hex_Encoded_Wasm()
    {
        var compiler = new PassthroughCompiler();
        var hex = Convert.ToHexString(WasmTestHelpers.BuildEmptyModule()).ToLowerInvariant();

        var result = await compiler.CompileAsync(hex);

        Assert.NotEmpty(result);
        Assert.Equal(0x00, result[0]);
        Assert.Equal(0x61, result[1]); // 'a'
        Assert.Equal(0x73, result[2]); // 's'
        Assert.Equal(0x6D, result[3]); // 'm'
    }

    [Fact]
    public async Task Passthrough_Compiler_Accepts_Base64_Encoded_Wasm()
    {
        var compiler = new PassthroughCompiler();
        var b64 = Convert.ToBase64String(WasmTestHelpers.BuildEmptyModule());

        var result = await compiler.CompileAsync(b64);

        Assert.NotEmpty(result);
        Assert.Equal(0x00, result[0]);
    }

    [Fact]
    public async Task Passthrough_Compiler_Rejects_Empty_Source()
    {
        var compiler = new PassthroughCompiler();
        await Assert.ThrowsAsync<CompilationException>(() => compiler.CompileAsync(""));
    }

    [Fact]
    public async Task Passthrough_Compiler_Rejects_Invalid_Hex()
    {
        var compiler = new PassthroughCompiler();
        await Assert.ThrowsAsync<CompilationException>(() => compiler.CompileAsync("not-hex-zzzz"));
    }

    [Fact]
    public async Task Compiler_Registry_Resolves_By_Language()
    {
        var registry = new CompilerRegistry()
            .Register(new PassthroughCompiler())
            .Register(new CSharpCompiler())
            .Register(new PythonCompiler());

        Assert.NotNull(registry.Resolve("wasm"));
        Assert.NotNull(registry.Resolve("csharp"));
        Assert.NotNull(registry.Resolve("python"));
        Assert.Null(registry.Resolve("rust"));
    }

    [Fact]
    public async Task WasmTool_Executes_Precompiled_Wasm_Module()
    {
        var sandbox = new WasmtimeSandbox();
        var registry = new CompilerRegistry().Register(new PassthroughCompiler());
        var tool = new WasmTool(sandbox, registry);

        var hex = Convert.ToHexString(WasmTestHelpers.BuildEmptyModule()).ToLowerInvariant();

        var result = await tool.ExecuteAsync(new WasmToolRequest(hex, "wasm"));

        Assert.True(result.Compiled, $"Compile error: {result.CompilationError}");
        Assert.True(result.Execution.IsSuccess, $"Exec error: {result.Execution.Stderr}");
        Assert.Null(result.CompilationError);
        Assert.True(result.TotalDuration.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task WasmTool_Caches_Compiled_Modules_By_Source_Hash()
    {
        var sandbox = new WasmtimeSandbox();
        var registry = new CompilerRegistry().Register(new PassthroughCompiler());
        var tool = new WasmTool(sandbox, registry);

        var hex = Convert.ToHexString(WasmTestHelpers.BuildEmptyModule()).ToLowerInvariant();

        var r1 = await tool.ExecuteAsync(new WasmToolRequest(hex, "wasm"));
        var r2 = await tool.ExecuteAsync(new WasmToolRequest(hex, "wasm"));

        Assert.True(r1.Compiled);
        Assert.True(r2.Compiled);
        // Второй запуск использует кеш — compile duration должен быть ~0
        Assert.True(r2.CompileDuration <= r1.CompileDuration,
            $"Expected cached run to be no slower than first. r1={r1.CompileDuration.TotalMilliseconds}ms, r2={r2.CompileDuration.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task WasmTool_Rejects_Unsupported_Language()
    {
        var sandbox = new WasmtimeSandbox();
        var registry = new CompilerRegistry().Register(new PassthroughCompiler());
        var tool = new WasmTool(sandbox, registry);

        var result = await tool.ExecuteAsync(new WasmToolRequest("anything", "haskell"));

        Assert.False(result.Compiled);
        Assert.NotNull(result.CompilationError);
        Assert.Contains("Unsupported", result.CompilationError);
    }

    [Fact]
    public async Task Approval_Gate_Can_Reject_Execution()
    {
        var sandbox = new WasmtimeSandbox();
        var registry = new CompilerRegistry().Register(new PassthroughCompiler());
        var rejectingGate = new TestApprovalGate(ApprovalDecision.Rejected);
        var tool = new WasmTool(sandbox, registry, rejectingGate);

        var hex = Convert.ToHexString(WasmTestHelpers.BuildEmptyModule()).ToLowerInvariant();

        var result = await tool.ExecuteAsync(new WasmToolRequest(hex, "wasm"));

        Assert.False(result.Compiled);
        Assert.Equal("User rejected execution", result.CompilationError);
        Assert.True(rejectingGate.Called);
    }

    [Fact]
    public async Task Approval_Gate_AutoApprove_By_Default()
    {
        var sandbox = new WasmtimeSandbox();
        var registry = new CompilerRegistry().Register(new PassthroughCompiler());
        var tool = new WasmTool(sandbox, registry); // no gate → auto-approve

        var hex = Convert.ToHexString(WasmTestHelpers.BuildEmptyModule()).ToLowerInvariant();

        var result = await tool.ExecuteAsync(new WasmToolRequest(hex, "wasm"));

        Assert.True(result.Compiled);
        Assert.True(result.Execution.IsSuccess);
    }

    private sealed class TestApprovalGate : ICodeApprovalGate
    {
        private readonly ApprovalDecision _decision;
        public bool Called { get; private set; }

        public TestApprovalGate(ApprovalDecision decision) => _decision = decision;

        public Task<ApprovalDecision> RequestApprovalAsync(
            WasmToolRequest request,
            IReadOnlyList<string> previousApprovals,
            CancellationToken ct = default)
        {
            Called = true;
            return Task.FromResult(_decision);
        }
    }
}
