# Task 54 — Централизованные логи и трейсы

**Phase:** 5
**Initiative:** 33
**Status:** in_progress
**Owner:** —
**Slug:** `centralized-observability`

## Goal
Каждый запрос и inter-agent вызов имеет traceId; метрики, трейсы и redacted логи отгружаются через OTLP в Grafana/Loki, Jaeger или облачные сервисы.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Sub-tasks
- [ ] `MeshCentralizedObservabilityConfig` class in `AppConfig.cs`
- [ ] `MeshCentralizedObservability` property added to `MeshConfig` class
- [ ] `IMeshObservabilityService` interface in `src/agent/Mesh/Observability/`
- [ ] `MeshObservabilityService` implementation (span creation, tag enrichment, metrics)
- [ ] `TraceContextPropagator` (W3C TraceContext + B3 injection/extraction)
- [ ] `TraceContextCarrier` model for passing trace info across hops
- [ ] Trace context headers wired into `HttpTransportAdapter`
- [ ] `MeshObservability` section added to `appsettings.json`
- [ ] DI registration in `MeshServiceExtensions.cs`
- [ ] Unit tests for `MeshObservabilityService` and `TraceContextPropagator` (Phase4Tests/Observability/)
- [ ] `dotnet build` + `dotnet test` pass

## Scope / Likely files
src/agent/Observability/Otlp/

## Dependencies
- блокирует / опирается на: [task_013 — opentelemetry](task_013.md)
- блокирует / опирается на: [task_041 — inter-agent-audit](task_041.md)

## Risks / Rollback
Sensitive PII в логах; централизованная redaction.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
