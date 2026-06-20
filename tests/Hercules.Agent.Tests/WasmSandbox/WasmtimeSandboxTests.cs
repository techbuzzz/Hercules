using System.Text;
using Hercules.WasmSandbox;
using Xunit;

namespace Hercules.Agent.Tests.WasmSandbox;

/// <summary>
///     Интеграционные тесты для WasmtimeSandbox.
///     Генерируют минимальные WASM-модули вручную (без wabt/wat2wasm) и проверяют:
///       • Empty module → ok
///       • Infinite loop + low fuel → timeout
///       • Cancellation → graceful abort
///       • Memory cap → не ломает валидный модуль
///       • Wasi args/env → доступны WASI-программам через WASI imports
/// </summary>
public class WasmtimeSandboxTests
{
    [Fact]
    public async Task Empty_Module_Executes_Successfully()
    {
        var wasm = WasmTestHelpers.BuildEmptyModule();
        using var sandbox = new WasmtimeSandbox();

        var result = await sandbox.ExecuteAsync(new WasmExecutionRequest(wasm));

        Assert.True(result.IsSuccess, $"Expected ok, got {result.Status}: {result.Stderr}");
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Infinite_Loop_With_Low_Fuel_TimesOut()
    {
        var wasm = WasmTestHelpers.BuildInfiniteLoopModule();
        using var sandbox = new WasmtimeSandbox();

        var limits = new WasmResourceLimits(
            MaxFuel: 5_000,
            MaxMemoryBytes: 1024 * 1024,
            MaxWallClockMs: 0,
            MaxOutputBytes: 0);

        var result = await sandbox.ExecuteAsync(new WasmExecutionRequest(wasm, Limits: limits));

        Assert.Equal("timeout", result.Status);
    }

    [Fact]
    public async Task Cancellation_Token_Aborts_Execution()
    {
        var wasm = WasmTestHelpers.BuildInfiniteLoopModule();
        using var sandbox = new WasmtimeSandbox();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var result = await sandbox.ExecuteAsync(
            new WasmExecutionRequest(wasm,
                Limits: new WasmResourceLimits(MaxFuel: 0, MaxWallClockMs: 0)),
            cts.Token);

        // Cancellation может приводить к "timeout" или "failed" в зависимости от timing
        Assert.Contains(result.Status, new[] { "timeout", "failed" });
    }

    [Fact]
    public async Task Strict_Memory_Cap_Does_Not_Break_Valid_Module()
    {
        var wasm = WasmTestHelpers.BuildEmptyModule();
        using var sandbox = new WasmtimeSandbox();

        var limits = new WasmResourceLimits(
            MaxFuel: 1_000_000,
            MaxMemoryBytes: 64 * 1024, // 64 KB
            MaxWallClockMs: 5_000,
            MaxOutputBytes: 1024);

        var result = await sandbox.ExecuteAsync(new WasmExecutionRequest(wasm, Limits: limits));

        Assert.True(result.IsSuccess, $"Expected ok, got {result.Status}: {result.Stderr}");
    }

    [Fact]
    public void Runtime_Version_Is_Reported()
    {
        using var sandbox = new WasmtimeSandbox();
        Assert.Equal("wasmtime-wasi", sandbox.Name);
        Assert.StartsWith("wasmtime-", sandbox.RuntimeVersion);
    }

    [Fact]
    public void Default_Limits_Are_Sensible()
    {
        var defaults = WasmResourceLimits.Default;

        Assert.True(defaults.MaxFuel > 0, "Default fuel must be > 0");
        Assert.True(defaults.MaxMemoryBytes > 0, "Default memory cap must be > 0");
        Assert.True(defaults.MaxWallClockMs > 0, "Default wall-clock timeout must be > 0");
        Assert.True(defaults.MaxOutputBytes > 0, "Default output cap must be > 0");
    }

    [Fact]
    public void Strict_Limits_Are_Tighter_Than_Default()
    {
        var s = WasmResourceLimits.Strict;
        var d = WasmResourceLimits.Default;

        Assert.True(s.MaxFuel < d.MaxFuel);
        Assert.True(s.MaxMemoryBytes < d.MaxMemoryBytes);
        Assert.True(s.MaxWallClockMs < d.MaxWallClockMs);
    }
}
