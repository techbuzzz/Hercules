using System.Diagnostics;
using System.Text;
using Wasmtime;

namespace Hercules.WasmSandbox;

/// <summary>
///     WASM sandbox на базе Wasmtime.NET v14 (Bytecode Alliance, MIT).
///     Capability-based security с WASI:
///       • Fuel limit (CPU instruction counting) — Store.AddFuel
///       • Epoch interruption (wall-clock deadline) — Store.SetEpochDeadline + Engine.IncrementEpoch
///       • Memory cap через Store.SetLimits
///       • Filesystem: DENY (WasiConfiguration без preopened dirs)
///       • Network: DENY (WasiConfiguration без inherit network)
///       • Environment: allow-list переменных через WasiConfiguration.WithEnvironmentVariable
///     API Wasmtime.NET v14: Engine(Config), Store, Linker.DefineWasi.
/// </summary>
public sealed class WasmtimeSandbox : IWasmSandbox, IDisposable
{
    public string Name => "wasmtime-wasi";

    public string RuntimeVersion => "wasmtime-14.0.0";

    private readonly Engine _engine;
    private readonly WasmResourceLimits _defaultLimits;
    private bool _disposed;

    public WasmtimeSandbox(WasmResourceLimits? defaultLimits = null)
    {
        var config = new Wasmtime.Config()
            .WithFuelConsumption(true)
            .WithEpochInterruption(true)
            .WithReferenceTypes(true)
            .WithSIMD(true)
            .WithMultiValue(true)
            .WithBulkMemory(true);

        _engine = new Engine(config);
        _defaultLimits = defaultLimits ?? WasmResourceLimits.Default;
    }

    public Task<WasmExecutionResult> ExecuteAsync(WasmExecutionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Wasmtime.NET API — синхронный. Запускаем в фоновом потоке, чтобы поддержать CancellationToken.
        return Task.Run(() => ExecuteCore(request, ct), ct);
    }

    private WasmExecutionResult ExecuteCore(WasmExecutionRequest request, CancellationToken ct)
    {
        var limits = request.Limits ?? _defaultLimits;
        var sw = Stopwatch.StartNew();

        // Stdout/stderr → файлы (WasiConfiguration принимает string path)
        var stdoutFile = Path.Combine(Path.GetTempPath(), $"hercules-stdout-{Guid.NewGuid():N}.txt");
        var stderrFile = Path.Combine(Path.GetTempPath(), $"hercules-stderr-{Guid.NewGuid():N}.txt");
        File.WriteAllText(stdoutFile, "");
        File.WriteAllText(stderrFile, "");

        long fuelConsumed = 0;
        var epochInterrupted = false;

        Store? store = null;
        try
        {
            // 1. Компилируем модуль (тяжёлая операция, в проде — кэшировать по hash)
            using var module = Module.FromBytes(_engine, "hercules-sandbox", request.Module);

            // 2. Store
            store = new Store(_engine);

            // 3. Fuel limit
            var fuelAmount = limits.MaxFuel > 0 ? (ulong)limits.MaxFuel : 1_000_000_000_000UL;
            store.AddFuel(fuelAmount);

            // 4. Memory cap. SetLimits(memorySize, tableElements, ?, ?, ?) — 5 nullable позиционных.
            if (limits.MaxMemoryBytes > 0)
            {
                store.SetLimits(
                    (long)limits.MaxMemoryBytes, // total memory bytes
                    (uint?)10_000,               // max table elements
                    null, null, null);
            }

            // 5. Epoch deadline (wall-clock в единицах engine epoch)
            if (limits.MaxWallClockMs > 0)
            {
                // deadline = 0 = прерывание при первом же IncrementEpoch() сразу после invoke.
                // Цикл epoch-проверок в wasmtime ловит это между инструкциями.
                store.SetEpochDeadline(1);
                StartEpochTicker(_engine, limits.MaxWallClockMs, () => epochInterrupted = true, ct);
            }

            // 6. Linker + WASI
            using var linker = new Linker(_engine);
            linker.DefineWasi();

            // 7. WASI config: stdout/stderr → файлы, args/env → allow-list. БЕЗ preopened dirs.
            var wasiConfig = new WasiConfiguration()
                .WithStandardOutput(stdoutFile)
                .WithStandardError(stderrFile);

            if (request.Args is { Length: > 0 })
            {
                wasiConfig = wasiConfig.WithArgs(request.Args);
            }

            if (request.Environment is { Count: > 0 })
            {
                foreach (var (k, v) in request.Environment)
                {
                    wasiConfig = wasiConfig.WithEnvironmentVariable(k, v);
                }
            }

            store.SetWasiConfiguration(wasiConfig);

            // 8. Instantiate
            linker.Instantiate(store, module);

            // 9. Entry point: "_start" для WASI, можно кастомное имя
            var entry = linker.GetDefaultFunction(store, request.EntryPoint)
                ?? throw new InvalidOperationException($"Entry point '{request.EntryPoint}' not found");

            // 10. Запускаем
            entry.Invoke();

            sw.Stop();
            fuelConsumed = (long)store.GetConsumedFuel();

            var stdout = ReadAndTruncate(stdoutFile, limits.MaxOutputBytes);
            var stderr = ReadAndTruncate(stderrFile, limits.MaxOutputBytes);

            if (epochInterrupted || sw.ElapsedMilliseconds >= limits.MaxWallClockMs - 100)
            {
                return WasmExecutionResult.TimedOut(sw.ElapsedMilliseconds, fuelConsumed, 0);
            }

            return new WasmExecutionResult(
                ExitCode: 0,
                Stdout: stdout,
                Stderr: stderr,
                DurationMs: sw.ElapsedMilliseconds,
                Status: "ok",
                FuelConsumed: fuelConsumed,
                PeakMemoryBytes: 0);
        }
        catch (WasmtimeException ex) when (ex.Message.Contains("all fuel exhausted", StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return WasmExecutionResult.TimedOut(sw.ElapsedMilliseconds, fuelConsumed, 0);
        }
        catch (WasmtimeException ex) when (epochInterrupted || ex.Message.Contains("epoch", StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return WasmExecutionResult.TimedOut(sw.ElapsedMilliseconds, fuelConsumed, 0);
        }
        catch (WasmtimeException ex) when (ex.Message.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return WasmExecutionResult.Killed($"OOM: {ex.Message}");
        }
        catch (WasmtimeException ex)
        {
            sw.Stop();
            return new WasmExecutionResult(
                ExitCode: 1,
                Stdout: ReadAndTruncate(stdoutFile, limits.MaxOutputBytes),
                Stderr: ReadAndTruncate(stderrFile, limits.MaxOutputBytes).Length > 0
                    ? ReadAndTruncate(stderrFile, limits.MaxOutputBytes)
                    : $"Wasm trap: {ex.Message}",
                DurationMs: sw.ElapsedMilliseconds,
                Status: epochInterrupted ? "timeout" : "failed",
                FuelConsumed: fuelConsumed,
                PeakMemoryBytes: 0);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return WasmExecutionResult.Failed($"Sandbox error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            store?.Dispose();
            TryDelete(stdoutFile);
            TryDelete(stderrFile);
        }
    }

    private static void StartEpochTicker(Engine engine, int deadlineMs, Action onInterrupt, CancellationToken ct)
    {
        // Engine.IncrementEpoch() — прерывает все stores с epoch interruption после deadline.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(deadlineMs, ct);
                if (!ct.IsCancellationRequested)
                {
                    engine.IncrementEpoch();
                    onInterrupt();
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    private static string ReadAndTruncate(string path, int maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return "";
            var bytes = File.ReadAllBytes(path);
            if (maxBytes > 0 && bytes.Length > maxBytes)
            {
                return Encoding.UTF8.GetString(bytes, 0, maxBytes) + "\n[... output truncated]";
            }
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "";
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
    }
}
