# Task 85 — OpenTelemetry polish: console gating, process instrumentation, histogram buckets, async logging

**Phase:** 5
**Initiative:** 46
**Status:** pending
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
- [ ] `Observability/OtelHostBuilderExtensions.cs:69,97` — gate console exporter: `if (string.IsNullOrEmpty(config.OtlpEndpoint)) { tracing.AddConsoleExporter(...); metrics.AddConsoleExporter(...); }`. Если OTLP настроен — только OTLP.
- [ ] `Observability/OtelHostBuilderExtensions.cs` — добавить `.AddProcessInstrumentation()` (CPU, memory, threads). Пакет уже referenced.
- [ ] `Observability/OtelMetrics.cs:46-78` — set explicit histogram bucket boundaries:
  - Latency (ms): `[5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000]`
  - Tokens: `[10, 50, 100, 500, 1000, 2000, 4000, 8000, 16000, 32000]`
  - Use `Histogram<long>.Create(..., unit: "ms", explicitBucketBoundaries: ...)`.
- [ ] `Program.cs` / `WebApi/Program.cs` — configure async logging: `builder.Logging.AddJsonConsole(o => { o.JsonWriter = ...; o.IncludeScopes = true; })` с async queue ИЛИ добавить OTLP log exporter (`AddOpenTelemetry().WithLogging(l => l.AddOtlpExporter())`).
- [ ] `LLM/ResilientLLMClient.cs:176,190,198,222,248` — add sampled logging: emit `LogWarning` 1-in-N (default N=10) + increment `LlmRetryCounter` metric always. Use `Interlocked.Increment(ref _retryLogCount)` + `if (count % 10 == 0) _logger.LogWarning(...)`.
- [ ] `Quotas/QuotaGuard.cs:54,74` — аналогично: sampled warning для soft violations (>80% usage).
- [ ] `Agent/AgentCore.cs:260,300,402,549` — sampled warnings для guardrail/quota/timeout.
- [ ] `appsettings.json` — `Otel.ConsoleExporterEnabled` (default: true если OtlpEndpoint empty, false иначе), `Logging.SampleRate` (default 10).
- [ ] Убрать `Console.WriteLine` в `Program.cs:555,559,...` и `WebApi/Program.cs:641,652,...` — заменить на `ILogger` structured logging.
- [ ] Unit-тест: OTLP endpoint set → console exporter not added (check via reflection on provider).
- [ ] Unit-тест: 100 retries → `LogWarning` called 10 times (1-in-10), `LlmRetryCounter` incremented 100 times.
- [ ] `dotnet build` + `dotnet test` pass.

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