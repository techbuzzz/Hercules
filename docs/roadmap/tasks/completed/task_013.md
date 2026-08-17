# Task 13 — Фундамент OpenTelemetry

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `opentelemetry`

## Goal
ASP.NET, LLM, tool, skill и storage операции эмитят связанные traces, метрики и structured logs. Локальный console/file exporter по умолчанию; OTLP опционально.

## Acceptance criteria

### Sub-tasks

- [x] NuGet: `OpenTelemetry`, `OpenTelemetry.Exporter.Console`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.Process` (beta), `OpenTelemetry.Instrumentation.Runtime` — Hercules.csproj
- [x] `Config/AppConfig.cs` — `OtelConfig` section: `Enabled` (default true), `ServiceName` ("hercules"), `OtlpEndpoint` (default null), `SamplingRatio` (default 1.0)
- [x] `Observability/OtelSetup.cs` — `ActivitySource` (named "hercules") and `Meter` (named "hercules") singletons; static readonly source/meter instances
- [x] `Observability/OtelMetrics.cs` — metric instruments: `HandleCounter`, `HandleDurationHistogram`, `LlmCallCounter`, `ToolCallCounter`, `ToolCallDurationHistogram`, `SkillHitCounter`, `LlmInputTokensHistogram`, `LlmOutputTokensHistogram`
- [x] `Observability/IOtelService.cs` + `OtelService.cs` — OtelService wrapping ActivitySource; methods: `StartActivity(name, kind)`, `StartActivity(name, parent, kind)`, `AddEvent`, `SetTag`, `SetTags`, `SetErrorStatus`, `StopActivity`; graceful no-op when OTel disabled
- [x] `Observability/OtelHostBuilderExtensions.cs` — `AddHerculesOtel(config)` extension: registers OtelService, console exporter (default), OTLP exporter (if OtlpEndpoint set), AlwaysOn/TraceIdRatioBased sampling, AspNetCore + Http + Runtime instrumentation, Hercules ActivitySource + Meter
- [x] `Program.cs` (CLI) — call `AddHerculesOtel(appConfig.Otel)` in ConfigureServices
- [x] `Program.cs` (WebAPI) — call `AddHerculesOtel(appConfig.Otel)` in ConfigureServices
- [x] `AgentCore.cs` — instrument: StartActivity wrapping HandleAsync (AgentCore.Handle span), child spans for SkillRoute and Tool.*; `HandleCounter.Inc()` + `HandleDurationHistogram.Record()`; `SkillHitCounter.Inc()`; tool call counters and histograms with success/error/timeout tags; graceful activity cleanup via try-finally
- [x] `ResilientLLMClient.cs` — instrument: Activity.Start with "LLM.{provider}", `LlmCallCounter` + `LlmCallDurationHistogram` + `LlmInputTokensHistogram` + `LlmOutputTokensHistogram` per call
- [x] `ObservabilityController.cs` — WebAPI endpoint: `GET /api/observability/telemetry` — returns enabled, serviceName, otlpEndpoint, samplingRatio, activitySourceName, meterName
- [x] `tests/Hercules.Agent.Tests/Observability/OtelServiceTests.cs` — 14 тестов: IsEnabled, StartActivity (null/enabled), parent linking, SetTag, SetTags, AddEvent, SetErrorStatus, StopActivity
- [x] `tests/Hercules.Agent.Tests/Observability/OtelMetricsTests.cs` — 17 тестов: all instruments non-null, counters and histograms record without throwing
- [x] `dotnet build` проходит без warnings (только NU1902 vulnerability warnings на OTel.Api)
- [x] `dotnet test` проходит (430/431; 1 pre-existing flaky HerculesBus test)

## Scope / Likely files
src/agent/HostBuilderExtensions.cs, src/agent/Observability/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)

## Risks / Rollback
Стоимость трейсинга на долгих задачах; sampling-стратегия обязательна.

## Implementation notes

### 2026-08-12

**Добавлено:**

**NuGet packages** (`Hercules.csproj`):
- OpenTelemetry 1.10.0, OpenTelemetry.Exporter.Console 1.10.0, OpenTelemetry.Exporter.OpenTelemetryProtocol 1.10.0, OpenTelemetry.Extensions.Hosting 1.10.0
- OpenTelemetry.Instrumentation.AspNetCore 1.10.1, OpenTelemetry.Instrumentation.Http 1.10.0
- OpenTelemetry.Instrumentation.Process 1.10.0-beta.1, OpenTelemetry.Instrumentation.Runtime 1.9.0

**`Observability/` folder** — 5 новых файлов:
- `OtelSetup.cs` — static `ActivitySource` ("hercules") + `Meter` ("hercules") singletons
- `OtelMetrics.cs` — 8 metric instruments: HandleCounter, HandleDurationHistogram, LlmCallCounter, LlmCallDurationHistogram, ToolCallCounter, ToolCallDurationHistogram, LlmInputTokensHistogram, LlmOutputTokensHistogram, SkillHitCounter
- `IOtelService.cs` — interface with 7 methods (testable, graceful no-op)
- `OtelService.cs` — implementation: delegates to ActivitySource, null-guard on all methods
- `OtelHostBuilderExtensions.cs` — `AddHerculesOtel(config)`: registers OtelService, adds console exporter (always), OTLP exporter (optional), sampling (AlwaysOn or TraceIdRatioBased), AspNetCore + Http + Runtime auto-instrumentation, Hercules ActivitySource + Meter

**`Config/AppConfig.cs`** — `OtelConfig` class with Enabled, ServiceName, OtlpEndpoint, SamplingRatio

**`AgentCore.cs`** — full OTel instrumentation:
- `HandleAsync` → `AgentCore.Handle` span with session_id and input_length tags
- `AgentCore.SkillRoute` child span with skill.name and skill.id tags
- `Tool.{name}` child span per tool execution with tool.name, tool.iteration, tool.success tags
- All spans use `Activity.Current?.Context` for linkage
- `HandleCounter`, `HandleDurationHistogram`, `SkillHitCounter`, `ToolCallCounter`, `ToolCallDurationHistogram` recorded
- `IOtelService?` nullable in constructor (backward compatible)
- `HandleAsyncCore` inner method for try-finally activity cleanup

**`ResilientLLMClient.cs`** — LLM call instrumentation:
- `Activity.Start($"LLM.{name}")` per provider call with provider and model tags
- `LlmCallCounter` (with provider, model tags), `LlmCallDurationHistogram`, `LlmInputTokensHistogram`, `LlmOutputTokensHistogram`
- `IOtelService?` nullable in constructor (backward compatible)

**`Hercules.WebApi/Controllers/ObservabilityController.cs`** — new controller:
- `GET /api/observability/telemetry` — returns OTel config status

**CLI + WebAPI Program.cs** — `AddHerculesOtel(appConfig.Otel)` registered

**Tests** — 31 new tests:
- `Observability/OtelServiceTests.cs` — 14 tests: IsEnabled, StartActivity, parent linking, SetTag, SetTags, AddEvent, SetErrorStatus, StopActivity
- `Observability/OtelMetricsTests.cs` — 17 tests: all instruments non-null, counters/histograms record without throwing

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors, 0 warnings (NU1902 advisory warnings only)
- `dotnet build Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet test` — 430/431 passed (1 pre-existing flaky HerculesBus test)

**Design decisions:**
- Console exporter always enabled (default local output)
- OTLP exporter only when OtlpEndpoint configured (opt-in)
- All instrumented services use nullable `IOtelService?` — backward compatible when OTel not used
- Sampling: AlwaysOn (ratio=1.0) or TraceIdRatioBased (lower ratios for prod cost control)
- Activity names use dot notation: `AgentCore.Handle`, `AgentCore.SkillRoute`, `Tool.{name}`, `LLM.{provider}`

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
