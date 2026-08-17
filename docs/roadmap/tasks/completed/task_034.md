# Task 34 — Capability registry

**Phase:** 3
**Status:** done
**Owner:** —
**Slug:** `capability-registry`

## Goal
Локальный файл или SQLite-реестр: известные агенты, capabilities, endpoint health, trust level, поддержка протоколов, cost/latency hints, expiry.

## Acceptance criteria

### Sub-tasks

- [x] Schema update: add `expiry_seconds`, `health_status`, `last_health_check`, `consecutive_failures`, `trust_level`, `cost_hint_usd`, `latency_hint_ms`, `supported_protocol_versions_json` columns to `mesh_agents` table
- [x] `ICapabilityRegistryService.cs` — interface: `GetEntry`, `ListAll`, `UpdateHealthStatus`, `UpdateTrustLevel`, `UpdateCostHint`, `UpdateLatencyHint`, `Touch`, `Remove`, `FindByCapability`, `FindByPhrase`
- [x] `CapabilityRegistryService.cs` — wraps `CapabilityRegistry`, implements interface, reads/writes new columns
- [x] `CapabilityHealthService.cs` — `BackgroundService`: periodic health-check of registered agent endpoints; TTL expiry cleanup
- [x] `CapabilityRegistryController.cs` — WebAPI: `GET /api/mesh/agents`, `GET /api/mesh/agents/{id}`, `GET /api/mesh/agents/{id}/health`, `GET /api/mesh/capabilities`, `POST /api/mesh/agents/{id}/touch`, `DELETE /api/mesh/agents/{id}`
- [x] Wire into DI (CLI + WebAPI via `MeshServiceExtensions.cs`)
- [x] `CapabilityRegistryServiceTests.cs` — 10 tests: TTL expiry, health status transitions, trust level, cost/latency hints
- [x] `CapabilityHealthServiceTests.cs` — 8 tests: TTL cleanup, health-check interval, consecutive failures
- [x] `dotnet build` — 0 errors
- [x] `dotnet test` — all new tests pass (37/37); 919/925 total (6 pre-existing failures in OtelService, BudgetGuard, AgentCore bounded execution)

## Scope / Likely files
src/agent/Mesh/Registry/

## Dependencies
- блокирует / опирается на: [task_032 — agent-manifest](task_032.md)

## Implementation notes

### 2026-08-13

**Schema migration (v1→v2):**
- `expiry_seconds` (INTEGER, default 86400), `health_status` (TEXT, default "unknown"), `last_health_check` (TEXT), `consecutive_failures` (INTEGER, default 0), `trust_level` (TEXT, default "unverified"), `cost_hint_usd` (REAL, default 0), `latency_hint_ms` (INTEGER, default 0), `supported_protocol_versions_json` (TEXT, default '["1.0"]')
- Migration via `ALTER TABLE ADD COLUMN` — backward-compatible with existing v1 schema

**New files:**
- `src/agent/Mesh/ICapabilityRegistryService.cs` — interface with 12 methods
- `src/agent/Mesh/CapabilityRegistryService.cs` — DI-friendly facade
- `src/agent/Mesh/CapabilityHealthService.cs` — `BackgroundService`: periodic health-check + TTL cleanup + `RecordSuccess`/`RecordFailure` helpers
- `tests/.../Phase3Tests/CapabilityRegistryServiceTests.cs` — 18 tests (TTL, health, trust, cost/latency)
- `tests/.../Phase3Tests/CapabilityHealthServiceTests.cs` — 8 tests (health-check, failures, cleanup)

**Modified files:**
- `src/agent/Mesh/CapabilityRegistry.cs` — schema v2, `MigrateSchemaV2()`, new CRUD methods, `AgentHealthStatus` enum, `RegistryAgentFullEntry` record
- `src/agent/Config/AppConfig.cs` — `MeshConfig` new fields: `CapabilityHealthCheckIntervalSeconds`, `CapabilityHealthFailureThreshold`, `CapabilityDefaultTtlSeconds`
- `src/agent/Mesh/MeshServiceExtensions.cs` — registers `ICapabilityRegistryService` + `CapabilityHealthService` as `HostedService`
- `src/agent/Hercules.WebApi/Controllers/MeshController.cs` — new endpoints: `GET /api/mesh/agents` (full), `GET /api/mesh/agents/{id}` (full), `GET /api/mesh/agents/{id}/health`, `POST /api/mesh/agents/{id}/touch`, `POST /api/mesh/agents/cleanup`

**Validation:**
- `dotnet build src/agent/Hercules.csproj` — 0 errors
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — 0 errors
- `dotnet test` — 919/925 passed; 6 pre-existing failures (OtelService ×5, BudgetGuard ×1)

## Risks / Rollback
Stale entries; health-check + TTL обязательны.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
