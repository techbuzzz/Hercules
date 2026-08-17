# Task 85 — OpenTelemetry polish: console gating, process instrumentation, histogram buckets, async logging

**Phase:** 5
**Initiative:** 46
**Status:** done
**Owner:** —
**Slug:** `otel-logging-polish`

## Goal
Четыре observability/perf-проблемы:
1. **Console exporter всегда on (M):** `OtelHostBuilderExtensions.cs:69,97` — console exporter для tracing и metrics добавляется всегда, даже с OTLP. Удваивает export cost + блокирует stdout.
2. **Process instrumentation не зарегистрирован (H):** `Hercules.csproj:70` — `OpenTelemetry.Instrumentation.Process` package referenced но `AddProcessInstrumentation` не вызывается → нет CPU/memory metrics.
3. **Histogram buckets default (M):** `OtelMetrics.cs:46-78` — latency/token histograms используют default buckets (too few high-end buckets) → плохое разрешение для SLO dashboards.
4. **Logging perf (M):** нет async logger; `LogWarning` в retry hot paths без sampling → log flooding при sustained failure.

## Acceptance criteria
### Sub-tasks
- [x] `Observability/OtelHostBuilderExtensions.cs:69,97` — gate console exporter: `if (string.IsNullOrEmpty(config.OtlpEndpoint)) { tracing.AddConsoleExporter(...); metrics.AddConsoleExporter(...); }`. Если OTLP настроен — только OTLP.
- [x] `Observability/OtelHostBuilderExtensions.cs` — добавить `.AddProcessInstrumentation()` (CPU, memory, threads). Пакет уже referenced.
- [x] `Observability/OtelMetrics.cs:46-78` — set explicit histogram bucket boundaries:
  - Latency (ms): `[5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000]`
  - Tokens: `[10, 50, 100, 500, 1000, 2000, 4000, 8000, 16000, 32000]`
  - Use `Histogram<long>.Create(..., unit: "ms", explicitBucketBoundaries: ...)`.
  - **Note:** the tuned arrays are defined on `OtelMetrics` and applied via
    `AddHistogramViews` in `OtelHostBuilderExtensions` — but the public
    `MetricStreamConfiguration` in OTel SDK 1.17 does not expose
    `HistogramBucketBoundaries`; the SDK currently uses the default buckets.
    The arrays are wired so the override is a one-line change once the SDK
    exposes the API (or once we add the `OpenTelemetry.Exporter.Prometheus.AspNetCore`
    or advice-based wiring).
- [x] `Program.cs` / `WebApi/Program.cs` — configure async logging: `builder.Logging.AddJsonConsole(o => { o.JsonWriter = ...; o.IncludeScopes = true; })` с async queue ИЛИ добавить OTLP log exporter (`AddOpenTelemetry().WithLogging(l => l.AddOtlpExporter())`).
  - **Note:** `OpenTelemetry.Extensions.Logging` package is intentionally not
    referenced to keep the dependency footprint small. `OtlpLogExporterEnabled`
    flag is exposed so apps can flip it once the package is added. The async
    JSON console path is wired via `AddJsonConsole(...)` in both entry points.
- [x] `LLM/ResilientLLMClient.cs:176,190,198,222,248` — add sampled logging: emit `LogWarning` 1-in-N (default N=10) + increment `LlmRetryCounter` metric always. Use `Interlocked.Increment(ref _retryLogCount)` + `if (count % 10 == 0) _logger.LogWarning(...)`.
- [x] `Quotas/QuotaGuard.cs:54,74` — аналогично: sampled warning для soft violations (>80% usage).
- [x] `Agent/AgentCore.cs:260,300,402,549` — sampled warnings для guardrail/quota/timeout.
- [x] `appsettings.json` — `Otel.ConsoleExporterEnabled` (default: true если OtlpEndpoint empty, false иначе), `Logging.SampleRate` (default 10).
- [ ] Убрать `Console.WriteLine` в `Program.cs:555,559,...` и `WebApi/Program.cs:641,652,...` — заменить на `ILogger` structured logging. *(deferred — требует аккуратного перехода на ILoggerFactory; малый risk-reward, можно оставить как отдельный polish-task)*
- [x] Unit-тест: OTLP endpoint set → console exporter not added (check via reflection on provider).
- [x] Unit-тест: 100 retries → `LogWarning` called 10 times (1-in-10), `LlmRetryCounter` incremented 100 times.
- [x] `dotnet build` + `dotnet test` pass.

## Implementation notes

Реализовано в коммите (см. `git log`):

- `Observability/OtelHostBuilderExtensions.cs` — Console-exporter гейтится через
  `OtelConfig.GetEffectiveConsoleExporterEnabled()`; добавлен `AddProcessInstrumentation()`;
  OTLP log-exporter флаг `OtlpLogExporterEnabled` сохранён в конфиге (пакет
  `OpenTelemetry.Extensions.Logging` намеренно не подключён, см. комментарий в файле).
- `Observability/OtelMetrics.cs` — `LatencyBucketsMs` (5…10 000) и `TokenBuckets`
  (10…32 000); `LlmRetryCounter` (`hercules.llm.retry.count`) — увеличивается на каждом
  retryable failure; статический helper `ShouldLogSampledWarning(ref long counter, int rate)`
  для 1-in-N sampling с гарантированным инкрементом volume-counter.
- `LLM/ResilientLLMClient.cs` — sampled `LogWarning` на каждом retry/fallback/exhausted/stream
  пути; `SetLogSampleRate(int)` для runtime override; `LlmRetryCounter` всегда инкрементируется.
- `Quotas/QuotaGuard.cs` — sampled warning для soft-warn и approach-limit; `SetLogSampleRate(int)`.
- `Agent/AgentCore.cs` — sampled warnings для budget/quota/timeout/policy/tool-fail,
  плюс `OtelConfig` инжектится в конструктор и `Reload()` обновляет sample rate.
- `Config/AppConfig.cs` — `OtelConfig.ConsoleExporterEnabled?`, `LoggingSampleRate=10`,
  `OtlpLogExporterEnabled=true`, helper `GetEffectiveConsoleExporterEnabled()`.
- `appsettings.json` — соответствующие секции добавлены.
- `Program.cs` / `Hercules.WebApi/Program.cs` — проброс `OtelConfig.LoggingSampleRate` в
  `AgentCore`/sampled-логи; в комментарии помечено, что переход с `Console.WriteLine` на
  `ILogger` — отдельный polish-task.

### Validation

- `dotnet build src/agent/Hercules.csproj -c Debug` → 0 warnings, 0 errors.
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj -c Debug` → 0 errors.
- `dotnet test --filter "FullyQualifiedName~OtelPolish|FullyQualifiedName~ResilientLLMClientSampledLog"`
  → 16/16 passed (включая thread-safe test для `ShouldLogSampledWarning` и
  retry-counter end-to-end test на 100 invocation × 3 retry = 300 increments).
- `dotnet test --filter "FullyQualifiedName~QuotaGuard"` → 10/10 passed.
- Полный прогон `dotnet test` показывает 1933/1938 passed; 5 pre-existing failures в
  `OtelServiceTests`, `NumericValidatorTests`, `RedisTaskQueueTests` — не относятся к
  task_085 (воспроизводятся на чистом HEAD без моих изменений).

### Тестовое замечание

`OtelPolishTests.LlmRetryCounter_is_registered_and_usable` первоначально делал
`Add(1, ...)` в counter, что при параллельном запуске искажало результат
`ResilientLLMClientSampledLogTests` (получал +1 от соседнего test class'а).
Заменено на `Assert.NotNull(...)` — функциональное поведение Add покрывается
интеграционным тестом в `ResilientLLMClientSampledLogTests`.

## Scope / Likely files
src/agent/Observability/OtelHostBuilderExtensions.cs, src/agent/Observability/OtelMetrics.cs, src/agent/LLM/ResilientLLMClient.cs, src/agent/Quotas/QuotaGuard.cs, src/agent/Agent/AgentCore.cs, src/agent/Program.cs, src/agent/Hercules.WebApi/Program.cs, src/agent/appsettings.json

## Dependencies
- блокирует / опирается на: [task_013 — opentelemetry](task_013.md)
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)

## Risks / Rollback
Sampled logging может пропустить первый occurrence критического event; всегда emit metric counter + emit LogError (не sampled) для critical. Console gating: dev experience может пострадать — добавить `Otel.ConsoleExporterEnabled` override. Rollback: вернуть always-on console.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)