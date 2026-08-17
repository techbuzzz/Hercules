# Task 54 — Централизованные логи и трейсы

**Phase:** 6
**Initiative:** 33
**Status:** done
**Owner:** —
**Slug:** `centralized-observability`

## Goal
Каждый запрос и inter-agent вызов имеет traceId; метрики, трейсы и redacted логи отгружаются через OTLP в Grafana/Loki, Jaeger или облачные сервисы.

## Acceptance criteria
- [x] TBD при старте работы (декомпозиция в sub-tasks)

## Sub-tasks
- [x] `MeshCentralizedObservabilityConfig` class in `AppConfig.cs`
- [x] `MeshCentralizedObservability` property added to `MeshConfig` class
- [x] `IMeshObservabilityService` interface in `src/agent/Mesh/Observability/`
- [x] `MeshObservabilityService` implementation (span creation, tag enrichment, metrics)
- [x] `TraceContextPropagator` (W3C TraceContext + B3 injection/extraction)
- [x] `TraceContextCarrier` model for passing trace info across hops
- [x] Trace context headers wired into `HttpTransportAdapter`
- [x] `MeshObservability` section added to `appsettings.json`
- [x] DI registration in `MeshServiceExtensions.cs`
- [x] Unit tests for `MeshObservabilityService` and `TraceContextPropagator` (Phase4Tests/Observability/)
- [x] `dotnet build` + `dotnet test` pass

## Scope / Likely files
src/agent/Observability/Otlp/

## Dependencies
- блокирует / опирается на: [task_013 — opentelemetry](task_013.md)
- блокирует / опирается на: [task_041 — inter-agent-audit](task_041.md)

## Risks / Rollback
Sensitive PII в логах; централизованная redaction.

## Implementation notes
### 2026-08-14

**`MeshCentralizedObservabilityConfig`** — new config in `AppConfig.cs`:
- Propagation format: w3c / b3 / both (W3C TraceContext + Zipkin B3)
- OTLP endpoints, span enrichment, mesh metrics, structured logs toggles
- MaxTagValueLength truncation, redacted attributes (payload never in spans)

**`TraceContextCarrier`** — cross-hop trace context model:
- TraceId, SpanId, W3C traceparent, tracestate, B3Sampled

**`TraceContextPropagator`** — W3C + B3 propagation:
- `Inject`: creates traceparent header (00-{traceId}-{spanId}-{flags}) + B3 headers
- `Extract`: parses W3C traceparent (version validation, length checks); B3 augments missing values
- `ToActivityContext`: converts carrier to ActivityContext for child span creation
- Safe fallback: unknown format → defaults to W3C

**`IMeshObservabilityService` / `MeshObservabilityService`** — central observability:
- `StartMeshSpan`: creates or child-span with mesh.* tags (intent, peer_agent_id, local_agent_span)
- `StartMeshSpanFromContext`: creates child span from incoming trace context
- `InjectTraceContext`: returns W3C/B3 headers dict for HTTP requests
- `ExtractTraceContext`: parses response headers into carrier
- `EnrichSpanWithMeshTags`: adds hop_count, delegation_depth, routing_decision, transport_kind
- `RecordMeshMetric`: Counter<long> for delegations, histograms for latency/hop_count

**`HttpTransportAdapter`** — trace context injected as HTTP headers before `SendAsync`:
- `IMeshObservabilityService` injected via constructor
- Headers added: traceparent, x-b3-traceid, x-b3-spanid, x-b3-sampled

**`TransportFactory`** — passes `IMeshObservabilityService` to `HttpTransportAdapter`

**Tests** — 43 tests:
- `TraceContextPropagatorTests`: 24 tests — inject, extract, W3C, B3, both, W3C priority, invalid versions, null handling, unknown format fallback
- `MeshObservabilityServiceTests`: 19 tests — enable/disable, span creation, trace injection/extraction, mesh events, metrics, truncation

**Validation**: `dotnet build -c Release`: 0 errors | `dotnet test MeshObservability*`: 43/43 | `dotnet test Phase4`: 275/277 (2 pre-existing NumericValidator failures)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
