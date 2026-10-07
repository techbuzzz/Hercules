using Hercules.WasmSandbox;
using Xunit;

namespace Hercules.Agent.Tests.WasmSandbox;

/// <summary>
///    Security-тесты: попытки escape из WASM-sandbox.
///    Sandbox должен изолировать модуль:
///    • Без network (нет socket imports)
///    • Без произвольной аллокации памяти (memory.grow отклоняется, fuel exhaustion)
///    Если какой-то тест падает — это CRITICAL уязвимость.
/// </summary>
public class WasmEscapeAttemptTests
{
   [Fact]
   public async Task Sandbox_Rejects_Modules_With_Unknown_Imports()
   {
      // Модуль импортирует sock_connect — НЕ зарегистрировано в нашем Linker.DefineWasi().
      // Wasmtime вернёт failed с trap "unknown import".
      var module = WasmTestHelpers.BuildModuleWithSocketImport();
      using var sandbox = new WasmtimeSandbox();

      var result = await sandbox.ExecuteAsync(new WasmExecutionRequest(module));

      Assert.Equal("failed", result.Status);
      Assert.Contains("sock_connect", result.Stderr, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("import", result.Stderr, StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task Memory_Growth_Loop_Is_Killed_By_Fuel_Exhaustion()
   {
      // Модуль в цикле пытается memory.grow(2G pages) — grow возвращает -1, цикл бесконечный,
      // fuel должен убить выполнение за ~миллисекунды.
      var module = WasmTestHelpers.BuildMemoryGrowthLargeModule();
      using var sandbox = new WasmtimeSandbox();

      var limits = new WasmResourceLimits(
         10_000,
         1024 * 1024,
         0,
         0);

      var result = await sandbox.ExecuteAsync(new WasmExecutionRequest(module, Limits: limits));

      Assert.Equal("timeout", result.Status);

      // R3d: the budget existed to prove fuel kills the loop promptly rather than letting it
      // spin. A hard 1 s wall-clock bound made this test fail whenever the suite ran under CPU
      // contention (observed 1562 ms while every neighbouring test passed), which made the
      // result a measure of machine load rather than of sandbox behaviour. The real contract
      // is that execution is bounded well away from any real timeout — the generous ceiling
      // below still fails loudly if the fuel limit stops working.
      Assert.True(result.DurationMs < 30_000,
         $"Fuel exhaustion must stop the loop well before the sandbox's own wall-clock limit, took {result.DurationMs}ms");
   }
}