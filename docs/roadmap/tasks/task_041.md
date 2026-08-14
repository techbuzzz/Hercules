# Task 41 — Inter-agent audit trail

**Phase:** 3
**Status:** done
**Owner:** —
**Slug:** `inter-agent-audit`

## Goal
Каждая делегация записывает sender, receiver, intent, payload hash, data classification, policy decision, cost, latency, response hash, исход; связано по traceId.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/Audit/InterAgentAuditRecord.cs` — record with all required fields: Sender, Receiver, Intent, PayloadHash, DataClassification, PolicyDecision, CostUsd, LatencyMs, ResponseHash, Outcome, TraceId, RootRequestId, DelegationDepth, HopCount, Timestamp
- [x] `src/agent/Mesh/Audit/IAuditSink.cs` — sink interface: `Task WriteAsync(InterAgentAuditRecord record, CancellationToken ct)`
- [x] `src/agent/Mesh/Audit/MeshAuditConfig.cs` — config: Enabled, Sinks (Serilog/Otel/File), PayloadHashEnabled, RedactPayloads, MaxPayloadLength, CostTrackingEnabled
- [x] `src/agent/Mesh/Audit/SerilogMeshAuditSink.cs` — structured log sink via existing ILogger<MeshAuditService>; emits `audit.mesh_delegation` structured events
- [x] `src/agent/Mesh/Audit/OpenTelemetryMeshAuditSink.cs` — OTel Activity span per delegation using existing `ActivitySource`; sets Tags for all non-sensitive fields
- [x] `src/agent/Mesh/Audit/FileMeshAuditSink.cs` — JSON Lines file (one JSON object per line); auto-rotates by date
- [x] `src/agent/Mesh/Audit/MeshAuditService.cs` — main service: `LogOutboundDelegationAsync`, `LogInboundDelegationAsync`, `LogTaskStateChangeAsync`, `LogDelegationResultAsync`; orchestrates all wired sinks
- [x] `Config/AppConfig.cs` — add `InterAgentAudit InterAgentAudit { get; set; } = new()` to `MeshConfig`
- [x] `Mesh/MeshServiceExtensions.cs` — wire `MeshAuditService`, register sinks based on config
- [x] `IntentRouter.cs` — inject `MeshAuditService`; call `LogOutboundDelegationAsync` after send, `LogDelegationResultAsync` after response received
- [x] `TaskLifecycleProtocol.cs` — inject `MeshAuditService`; call `LogTaskStateChangeAsync` on Accept/Complete/Fail/Cancel/Expire
- [x] `tests/Phase3Tests/MeshAuditTests.cs` — unit tests: MeshAuditService (all log methods), each sink, config defaults
- [x] `dotnet build src/agent/Hercules.csproj` — 0 errors
- [x] `dotnet test Phase3Tests` — all Phase 3 tests pass

## Implementation notes
- **Date:** 2026-08-13
- **Files created:** `src/agent/Mesh/Audit/InterAgentAuditRecord.cs`, `IAuditSink.cs`, `MeshAuditConfig.cs`, `SerilogMeshAuditSink.cs`, `OpenTelemetryMeshAuditSink.cs`, `FileMeshAuditSink.cs`, `MeshAuditService.cs` (6 files)
- **Tests:** `tests/Phase3Tests/MeshAuditTests.cs` (16 tests, all passing)
- **Modified:** `Config/AppConfig.cs`, `Mesh/MeshServiceExtensions.cs`, `Mesh/IntentRouter.cs`, `Mesh/TaskLifecycle/TaskLifecycleProtocol.cs`
- **3-sink architecture:** Serilog (structured logs), OpenTelemetry (Activity spans), File (JSON Lines, off by default)
- **SHA-256 payload hashing** with optional redaction; sampling via `SampleRate`; fire-and-forget for task state transitions

## Scope / Likely files
src/agent/Mesh/Audit/

## Dependencies
- блокирует / опирается на: [task_014 — audit-privacy](task_014.md)
- блокирует / опирается на: [task_035 — delegation-envelope](task_035.md)

## Risks / Rollback
Размер логов; ротация и архив.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
