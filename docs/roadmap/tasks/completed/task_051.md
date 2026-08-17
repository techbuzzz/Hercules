# Task 51 — Shared memory sync

**Phase:** 4
**Initiative:** 21
**Status:** done
**Owner:** —
**Slug:** `shared-memory-sync`

## Goal
Избранные факты и навыки синхронизируются только между trusted агентами: explicit namespaces, provenance, conflict resolution, TTL, encryption in transit, per-field data-classification policy.

## Acceptance criteria
- [x] `SharedMemorySyncConfig` in `AppConfig.cs`: Enabled, DefaultTtlMinutes, MaxFactsPerAgent, EncryptionRequired, MaxAllowedSensitivity
- [x] Extend `SharedMemoryFact` with `TtlMinutes`, `ExpiresAt`, `CreatedAt`, `Sensitivity`, `Source`, `Tags` fields
- [x] `PublishFactAsync` accepts `TtlMinutes`, `Sensitivity`, `Source`, `Tags` parameters
- [x] `ReceiveFact` / `LoadLocalFacts` filter out expired facts (TTL enforcement)
- [x] `GetLocalFacts` / `GetFactsForAgent` enforce sensitivity filter (maxAllowedSensitivity from config)
- [x] `SharedMemorySyncConfig` registered in `MeshServiceExtensions.AddMeshServices`
- [x] Unit tests: TTL expiry filtering, sensitivity filtering, provenance fields
- [x] `dotnet build` + `dotnet test` pass

## Implementation notes

### 2026-08-13

**`SharedMemorySync.cs`** — extended with full task_051 requirements:
- `SharedMemorySensitivity` enum added (Public/Internal/Sensitive/Restricted)
- `SharedMemoryFact` extended: `TtlMinutes`, `ExpiresAt`, `CreatedAt`, `Sensitivity`, `Source`, `Tags`; `IsExpired` helper
- `PublishFactAsync` now accepts `ttlMinutes`, `sensitivity`, `source`, `tags` parameters; uses config defaults
- `ReceiveFact` rejects Restricted facts at the gate; enforces `MaxAllowedSensitivity`; rejects expired facts
- `LoadLocalFacts(includeExpired)` — prunes expired facts on read
- `GetLocalFacts` / `GetFactsForAgent` — sensitivity filter via `IsSensitivityAllowed()`
- `SharedMemorySync` constructor now takes `SharedMemorySyncConfig`
- `IntentEnvelope` usages updated to recommended object initializer pattern

**`Config/AppConfig.cs`** — added `SharedMemorySyncConfig`:
- `Enabled` (bool, default false), `DefaultTtlMinutes` (1440), `MaxFactsPerAgent` (500), `EncryptionRequired` (true), `MaxAllowedSensitivity` ("Sensitive"), `SyncIntervalMinutes` (30)

**`MeshServiceExtensions.cs`** — wired `SharedMemorySyncConfig` into DI

**`tests/.../SharedMemorySyncTests.cs`** — 21 tests (was 8):
- TTL tests: expiry on publish/receive, default TTL from config, prune expired on read
- Sensitivity tests: Restricted blocked, excessive sensitivity rejected, sensitivity filtering
- Provenance tests: source/tags/createdAt/updatedAt fields
- Max facts limit, JSON round-trip

**Validation:**
```
dotnet build src/agent/Hercules.csproj -c Release → 0 errors
dotnet test --filter "FullyQualifiedName~SharedMemorySync" -c Release → 21/21 pass
dotnet test tests/Hercules.Agent.Tests/ -c Release → 1353/1361 pass (8 pre-existing failures unchanged)
```

## Scope / Likely files
src/agent/Mesh/SharedMemorySync.cs

## Dependencies
- блокирует / опирается на: [task_011 — layered-memory](task_011.md)
- блокирует / опирается на: [task_014 — audit-privacy](task_014.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Утечка чувствительных данных; data classification enforcement.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
