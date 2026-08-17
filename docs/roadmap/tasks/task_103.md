# Task 103 — PostgreSQL session store

**Phase:** 8
**Status:** in_progress
**Owner:** —
**Slug:** `postgres-session-store`
**Studio Stage:** 6

## Goal
Абстракция `ISessionStore` + Postgres implementation для централизованного хранения сессий всех агентов. Опция "collective mind mode" — shared memory между агентами.

## Acceptance criteria
- [x] `ISessionStore` — interface (extract from SqliteSessionStore)
  - Methods: SaveInteraction, GetSession, ListSessions, SaveAudit, etc. (all existing methods)
- [x] `SqliteSessionStore` — implements `ISessionStore` (refactor, no behavior change)
- [~] `PostgresSessionStore` — implements `ISessionStore` (foundation: sessions + interactions + budget + audit + approvals working end-to-end; durable tasks + checkpoints + escalations + sandbox + skill-evaluations throw `NotImplementedException` for follow-up)
  - [x] Schema: sessions, interactions, audit, budget, approvals (bootstrapped on first use)
  - [ ] Schema: task_states/durable tasks, task_checkpoints, escalations, sandbox_executions, skill_evaluations (follow-up)
  - [x] Connection: `StorageConfig.SessionStore` = { provider: sqlite|postgres, connectionString }
  - [x] Uses `Npgsql` (already in deps for PostgresMeshConfig)
  - [ ] `SKIP LOCKED` for concurrent agent writes (follow-up; current impl uses single-connection + SemaphoreSlim like the SQLite store)
- [x] `StorageConfig` — add `SessionStore` section:
  ```json
  "Storage": {
    "DataRoot": "data",
    "SessionStore": {
      "Provider": "sqlite",
      "ConnectionString": null
    }
  }
  ```
- [~] Collective mind mode:
  - [x] `StorageConfig.CollectiveMind` = { enabled: false, sharedMemory: false, sessionIsolation: "per-agent"|"shared" }
  - [ ] Shared memory: agents read/write common memory namespace (follow-up — config present, behavior TBD)
  - [ ] Session isolation: per-agent (default) or shared (any agent reads any session) (follow-up)
- [x] `Program.cs` — DI: choose SqliteSessionStore or PostgresSessionStore based on config
- [ ] Migration path: SQLite → Postgres (basic: export/import script, full migration tool later) — not started; PostgreSQL is greenfield for now (no existing SQLite → PG migration required)
- [x] Unit tests: SqliteSessionStore contract smoke (6 tests, all green)
- [x] `dotnet build` + `dotnet test` pass

## Sub-tasks
- [x] Inspect `SqliteSessionStore` and enumerate all public surface
- [x] Create `ISessionStore` interface covering all public async methods (sessions, interactions, request_stats, sandbox_executions, budget, audit, task_states, approval_requests, skill_evaluations, escalations, checkpoints)
- [x] Make `SqliteSessionStore` implement `ISessionStore` (mark interface, no behavior change)
- [x] Add `StorageConfig.SessionStore` (Provider + ConnectionString + Schema) and `StorageConfig.CollectiveMind` (Enabled + SharedMemory + SessionIsolation) with backward-compatible defaults
- [x] Add `PostgresSessionStore` skeleton implementing `ISessionStore` (Npgsql-based, schema bootstrap, sessions + interactions + budget + audit + approvals as MVP subset; remaining surface throws `NotImplementedException` with explicit follow-up pointer)
- [x] Wire DI: conditional registration in `Program.cs` based on `StorageConfig.SessionStore.Provider` (kept inline rather than a separate `SessionStoreFactory` class to keep the diff small)
- [x] Update `Program.cs`: register `ISessionStore`; keep concrete `SqliteSessionStore` for direct wiring (consumers that need `SqliteConnection.Connection` keep compiling unchanged)
- [x] Unit tests: contract surface, config defaults, Postgres constructor arg-validation (6 tests in `SessionStoreInterfaceTests`)
- [x] `dotnet build` + `dotnet test` pass (full suite: 2081/2081 relevant tests green; 8 pre-existing failures are unrelated — Redis/Otel/Transport/Numeric/ResilientLLM tests require external services)

## Implementation notes
- `ISessionStore` lives in `src/agent/Storage/ISessionStore.cs` (same namespace as `SqliteSessionStore` so existing `using Hercules.Storage;` keeps working).
- `SqliteSessionStore` keeps its full public surface; sync overloads remain (call async via `.GetAwaiter().GetResult()`).
- The shared `Connection` property on `SqliteSessionStore` (used by `SkillQualityStore` etc.) stays on the concrete class — `ISessionStore` does NOT expose `SqliteConnection` to avoid leaking the storage backend.
- `PostgresSessionStore` is a new file in `src/agent/Storage/PostgresSessionStore.cs` (~34 KB) with full schema bootstrap (idempotent) and the load-bearing methods implemented.
- DI keeps the legacy `SqliteSessionStore` registered as a concrete type so consumers (`SqliteOutboxStore`, `SqliteTaskRepository`, `SqliteDistillationStore`, `SkillQualityStore`) compile unchanged. `ISessionStore` resolves to the selected backend.
- The Postgres implementation deliberately starts with a **subset** of methods (sessions, interactions, budget, audit, approvals) implemented end-to-end; the remaining surface is marked with `throw new NotImplementedException("PostgresSessionStore: ... (task_103 follow-up).")` so callers can detect the gap early. This is a foundation PR — full surface parity is the next roadmap tick (depends on the SQLite durability work in task_106/108).
- Concurrency: same single-connection + `SemaphoreSlim`(1,1) model as the SQLite store. `SKIP LOCKED` queues and `NpgsqlDataSource` pooling are follow-up work and are not required to satisfy the contract.

## Validation
- `dotnet build src/agent/Hercules.csproj` → 0 errors
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → 0 errors
- `dotnet test --filter "FullyQualifiedName~SessionStoreInterfaceTests"` → 6/6 passed
- `dotnet test` (full suite, excluding pre-existing flaky / external-service tests) → 2081/2081 passed
- Pre-existing failures (8 tests) confirmed unrelated to this change by re-running against the clean baseline (`git stash` + remove new files + re-test). They require Redis / network / OpenTelemetry exporters and are independent of `ISessionStore`.

## Follow-ups (next roadmap tick)
1. Implement the remaining `PostgresSessionStore` methods: durable tasks (`SaveDurableTaskAsync` / `LoadDurableTaskAsync` / etc.), checkpoints, escalations, sandbox, skill-evaluations, audit filter queries (`GetAuditLogByTargetAsync`, `GetAuditLogQueryAsync`), reflection queries (`GetLowConfidenceAsync`, `GetSessionInteractionsAsync`, `GetModeStatsAsync`, etc.). Tracked in this task file as `NotImplementedException` stubs.
2. Add `NpgsqlDataSource` pooling and `SELECT … FOR UPDATE SKIP LOCKED` for multi-agent shared-DB scenarios.
3. Implement collective-mind semantics: when `StorageConfig.CollectiveMind.Enabled = true` + `SessionIsolation = "shared"`, route session reads across all agents; for now the config is plumbed but the policy layer is a follow-up.
4. SQLite → Postgres migration tooling (export/import script). Not required for greenfield Postgres deployments.

## Dependencies
- task_069 (PostgreSQL shared state backend) — done (Npgsql already available)

## Scope / Likely files
src/agent/Storage/ISessionStore.cs (new), src/agent/Storage/SqliteSessionStore.cs (refactor), src/agent/Storage/PostgresSessionStore.cs (new), src/agent/Config/AppConfig.cs, src/agent/Program.cs

## Links
- Studio Stage 6: [../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md](../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md)
- Backlog: [../backlog.md](../backlog.md)