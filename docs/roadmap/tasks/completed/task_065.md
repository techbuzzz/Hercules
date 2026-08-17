# Task 65 — Mesh observability

**Phase:** 4
**Initiative:** 25
**Status:** done
**Owner:** —
**Slug:** `mesh-observability`

## Goal
Каждый локальный и межагентный шаг эмитит коррелированные traces, метрики и структурированные логи. Mesh-трафик, routing-решения, ретраи и взаимодействия с хранилищами видимы и атрибутируемы per request и per agent.

## Acceptance criteria

### Sub-tasks

- [x] Wire `IMeshObservabilityService` into `CapabilityMeshRouter`: emit routing decision spans + `routing_decision` metrics per RouteAsync call
- [x] Wire `IMeshObservabilityService` into `ResilientTransport`: emit retry spans + `retry_attempt` + `circuit_breaker_state_change` metrics
- [x] Wire `IMeshObservabilityService` into `IntentRouter`: emit delegation spans (outbound/inbound) with trace context propagation
- [x] Add `IMeshObservabilityService` optional injection to `TaskLifecycleProtocol` for observability on task lifecycle events
- [x] Add `IMeshObservabilityService` optional injection to `FanOutOrchestrator` for fan-out spans
- [x] Add `MeshObservabilityController` with `GET /api/mesh/observability/status` endpoint
- [x] Wire `IMeshObservabilityService` in `MeshServiceExtensions` DI (already wired; verify)
- [x] Add unit tests in `tests/Phase4Tests/MeshObservabilityTests.cs` for router/router/health/transport observability
- [x] `dotnet build src/agent/Hercules.csproj` — 0 errors
- [x] `dotnet test` — all Phase 4 tests pass

## Scope / Likely files
src/agent/Mesh/Observability/, src/agent/Observability/MeshEnrichment.cs

## Dependencies
- блокирует / опирается на: [task_013 — opentelemetry](task_013.md)
- блокирует / опирается на: [task_041 — inter-agent-audit](task_041.md)
- блокирует / опирается на: [task_043 — mesh-router](task_043.md)

## Risks / Rollback
PII и секреты в трейсах; централизованная redaction + sampling policy.

## Implementation notes

**Date:** 2026-08-14

**`src/agent/Mesh/Router/MeshRouter.cs`** — added `IMeshObservabilityService?` optional dependency:
- `RouteAsync` emits `routing_decision` metric (0 when disabled, N candidates when enabled)
- `RouteCoreAsync` starts `MeshRouter.Route` span, enriches with top candidate agent ID
- `RecordMeshEvent("router.completed")` on span completion

**`src/agent/Mesh/Resilience/ResilientTransport.cs`** — added `IMeshObservabilityService?`:
- `SendAsync` starts `ResilientTransport.Send` span with peer + intent tags
- Per-attempt child spans: `ResilientTransport.Retry.{N}` with success/failure events
- `RecordMeshEvent("transport.success/failure")` per attempt with latency
- `RecordMeshMetric("retry_attempt")` before each retry
- Circuit-open rejection emits `resilience.circuit_open` event
- `resilience.exhausted` event when all retries fail

**`src/agent/Mesh/IntentRouter.cs`** — added `IMeshObservabilityService?`:
- `RouteAsync` starts `IntentRouter.Delegate` span for peer delegation
- `EnrichSpanWithMeshTags` with intent, sender, receiver, delegation depth, hop count
- `InjectTraceContext` for W3C/B3 propagation
- `delegation` counter, `delegation.success/failure` events, `delegation_latency_ms` histogram
- Trace context propagated via `TraceContextCarrier`

**`src/agent/Mesh/TaskLifecycle/TaskLifecycleProtocol.cs`** — added optional `IMeshObservabilityService` constructor param.

**`src/agent/Mesh/Router/FanOutOrchestrator.cs`** — added optional `IMeshObservabilityService`:
- `OrchestrateFanOutAsync` starts `FanOut.Orchestrate` span
- `fanout.no_peers` event when no peers available

**`src/agent/Mesh/Observability/MeshObservabilityService.cs`** — added `retry_attempt` case to `RecordMeshMetric`.

**`src/agent/Mesh/MeshServiceExtensions.cs`** — updated DI registrations to pass `IMeshObservabilityService`:
- `IntentRouter`, `TaskLifecycleProtocol`, `ResilientTransport`, `FanOutOrchestrator` — all get `sp.GetService<IMeshObservabilityService>()` (null-safe)

**`src/agent/Hercules.WebApi/Controllers/MeshObservabilityController.cs`** — new controller:
- `GET /api/mesh/observability/status` — enabled flag + full config
- `GET /api/mesh/observability/config` — basic enabled state

**`src/agent/Hercules.WebApi/Program.cs`** — added `app.MapMeshObservability()`.

**Tests:** `tests/Phase4Tests/MeshObservabilityTests.cs` — 9 tests (all pass):
- `MeshObservabilityService_RecordMeshMetric_RetryAttempt_NoThrow`
- `MeshObservabilityService_RecordMeshMetric_UnknownMetric_NoThrow`
- `MeshObservabilityService_IsEnabled_ReflectsConfig` (true/false)
- `CapabilityMeshRouter_RouteAsync_EmitsMetric_WhenDisabled`
- `CapabilityMeshRouter_RouteAsync_CallsObservabilityWhenEnabled`
- `CapabilityMeshRouter_RouteAsync_WithoutObservability_DoesNotThrow`
- `TraceContextCarrier_HasTrace_ReturnsFalse_WhenEmpty`
- `TraceContextCarrier_HasTrace_ReturnsTrue_WhenTraceIdSet`
- `TraceContextCarrier_AllPropertiesInitialized`

**Validation:**
- `dotnet build src/agent/Hercules.csproj` — 0 errors (pre-existing warnings only)
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — 0 errors
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` — 0 errors
- `dotnet test --filter "MeshObservabilityTests"` — 9/9 passed
- `dotnet test --filter "Phase4Tests"` — 284/286 passed (2 pre-existing `NumericValidatorTests` failures)
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
