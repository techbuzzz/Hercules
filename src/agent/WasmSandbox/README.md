# Hercules WASM Sandbox (V3.1)

Безопасное исполнение кода в WebAssembly runtime с capability-based isolation.
Заменяет прямой запуск процессов (`dotnet run --file`, `python`) на выполнение .wasm-модулей
через Wasmtime.NET v14 (Bytecode Alliance).

## Зачем

LLM-агент может сгенерировать `File.Delete("C:\\")` или `Process.Start("rm", "-rf", "/")`.
Direct execution — это `sudo rm -rf` с шахматными часами. WASM sandbox даёт:

- **Capability-based security** — модуль НЕ МОЖЕТ выйти за пределы явных capabilities
- **Resource limits** — fuel (CPU), memory cap, wall-clock deadline
- **Crash isolation** — trap в WASM не убивает host-процесс
- **Cross-language** — C#, Python, Rust, JS через единый .wasm артефакт

## Архитектура

```
┌─────────────────────────────────────────────────────────────┐
│ WasmTool (high-level API)                                  │
│   - approval gate (human-in-the-loop)                      │
│   - compile cache (по sha256(source))                      │
│   - metrics (compile duration, exec duration)              │
├─────────────────────────────────────────────────────────────┤
│ CompilerRegistry                                           │
│   - PassthroughCompiler (hex/base64 → .wasm)  ← v3.1      │
│   - CSharpCompiler (dotnet wasm-tools)        ← stub v3.2  │
│   - PythonCompiler (RustPython.wasm)          ← stub v3.2  │
├─────────────────────────────────────────────────────────────┤
│ WasmtimeSandbox                                            │
│   - Engine + Store (fuel, epoch, limits)                   │
│   - WASI preview1 (stdout/stderr/args/env allow-list)      │
│   - Network/Filesystem: DENY by default                    │
└─────────────────────────────────────────────────────────────┘
```

## Resource Limits (default)

| Лимит              | Default         | Strict          | Описание                          |
|--------------------|-----------------|-----------------|-----------------------------------|
| MaxFuel            | 1_000_000_000   | 100_000_000     | WASM-инструкции (≈10s / ≈1s CPU) |
| MaxMemoryBytes     | 100 MB          | 32 MB           | Linear memory cap                 |
| MaxWallClockMs     | 30_000          | 5_000           | Epoch interruption deadline       |
| MaxOutputBytes     | 1 MB            | 100 KB          | stdout + stderr cap               |

## Security guarantees

- **Filesystem: DENY.** WasiConfiguration без `WithPreopenedDirectory` → модуль не имеет fd на host FS.
- **Network: DENY.** WasiConfiguration без `WithInheritedNetwork` → нет socket imports.
- **Environment: ALLOW-LIST.** Только явно переданные `WasmToolRequest.Environment` переменные.
- **Capability isolation.** Любая попытка импортировать неизвестную функцию (socket, fork, и т.п.)
  → trap на linking stage → `ExecutionResult.Status = "failed"`.
- **Resource exhaustion.** Infinite loop → fuel exhaustion → `Status = "timeout"`.
- **Memory bomb.** Memory.grow за пределы `MaxMemoryBytes` → trap / fuel exhaustion.

## Acceptance Criteria (verified by 19 tests)

✅ Empty wasm module executes successfully (19 ms)
✅ Infinite loop with low fuel → timeout
✅ Cancellation token aborts execution
✅ Strict memory cap doesn't break valid module
✅ Module with unknown import (socket) → failed (link error)
✅ Memory growth loop → killed by fuel exhaustion
✅ WasmTool compiles + executes + caches by source hash
✅ Approval gate can reject execution
✅ Unsupported language rejected at compile stage

## Использование

```csharp
// Setup
var sandbox = new WasmtimeSandbox(WasmResourceLimits.Default);
var registry = new CompilerRegistry()
    .Register(new PassthroughCompiler())  // hex/base64 → .wasm
    .Register(new CSharpCompiler());     // stub пока
var tool = new WasmTool(sandbox, registry);

// Execute
var hex = Convert.ToHexString(myWasmBytes).ToLowerInvariant();
var result = await tool.ExecuteAsync(new WasmToolRequest(hex, "wasm"));

if (result.Execution.IsSuccess)
    Console.WriteLine(result.Execution.Stdout);
else
    Console.WriteLine($"Error: {result.Execution.Stderr}");
```

## Out of scope (V3.1)

- ❌ Real C# → WASM компиляция (planned V3.2 через `dotnet workload install wasm-tools`)
- ❌ Real Python → WASM (planned V3.2 через RustPython.wasm)
- ❌ JS/TS execution (planned V3.2)
- ❌ Persistent state между executions (planned V4 — stateful WASM modules)
- ❌ Network capabilities с whitelist (planned V3.2 после security review)

## Pitfalls (на стадии разработки)

1. **`SetLimits` сигнатура в Wasmtime.NET v14 — 5 nullable параметров.** Использовать positional,
   не именованные (`memory: ...` НЕ работает как kwarg).
2. **`memory.grow` без cap может попасть в неучтённый цикл.** Цикл `loop { memory.grow(N); drop; br 0 }`
   с N=1 — wasmtime НЕ тратит fuel per iteration; используйте `MaxMemoryBytes` cap.
3. **`Engine.IncrementEpoch()` прерывает между WASM-инструкциями.** Пустой модуль
   без инструкций может не прерываться — для коротких модулей используйте fuel, не epoch.
4. **`WasiConfiguration.WithStandardOutput(string path)`** — принимает путь к файлу, не stream.
   Stdout/stderr надо читать из временного файла после invoke.

## Файлы

- `IWasmSandbox.cs` — интерфейс sandbox + DTO (WasmExecutionRequest/Result, WasmResourceLimits)
- `WasmtimeSandbox.cs` — реализация на Wasmtime.NET v14 (MIT, Bytecode Alliance)
- `WasmTool.cs` — high-level API с approval gate + cache
- `Compilation/IWasmCompiler.cs` — интерфейс компилятора + CompilationException
- `Compilation/CompilerRegistry.cs` — реестр компиляторов по языкам
- `Compilation/CSharpCompiler.cs` — stub для V3.2
- `Compilation/PythonCompiler.cs` — stub для V3.2
- `Compilation/PassthroughCompiler.cs` — рабочий (hex/base64 → .wasm)

## Тесты

`tests/Hercules.Agent.Tests/WasmSandbox/`:
- `WasmtimeSandboxTests.cs` — 7 тестов базового API
- `WasmEscapeAttemptTests.cs` — 2 теста security (unknown import, memory bomb)
- `WasmToolTests.cs` — 10 тестов high-level API + cache + approval gate
- `WasmTestHelpers.cs` — генератор минимальных wasm-модулей без внешних зависимостей

Запуск:
```bash
dotnet test tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj
# Passed!  - Failed: 0, Passed: 19
```
