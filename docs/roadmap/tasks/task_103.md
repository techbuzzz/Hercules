# Task 103 — PostgreSQL session store

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `postgres-session-store`
**Studio Stage:** 6

## Goal
Абстракция `ISessionStore` + Postgres implementation для централизованного хранения сессий всех агентов. Опция "collective mind mode" — shared memory между агентами.

## Acceptance criteria
- [ ] `ISessionStore` — interface (extract from SqliteSessionStore)
  - Methods: SaveInteraction, GetSession, ListSessions, SaveAudit, etc. (all existing methods)
- [ ] `SqliteSessionStore` — implements `ISessionStore` (refactor, no behavior change)
- [ ] `PostgresSessionStore` — implements `ISessionStore`
  - Schema: sessions, interactions, audit, budget, approvals, tasks (same as SQLite)
  - Connection: `StorageConfig.SessionStore` = { provider: sqlite|postgres, connectionString }
  - Uses `Npgsql` (already in deps for PostgresMeshConfig)
  - `SKIP LOCKED` for concurrent agent writes (if shared DB)
- [ ] `StorageConfig` — add `SessionStore` section:
  ```json
  "Storage": {
    "DataRoot": "data",
    "SessionStore": {
      "Provider": "sqlite",
      "ConnectionString": null
    }
  }
  ```
- [ ] Collective mind mode:
  - `StorageConfig.CollectiveMind` = { enabled: false, sharedMemory: false, sessionIsolation: "per-agent"|"shared" }
  - Shared memory: agents read/write common memory namespace
  - Session isolation: per-agent (default) or shared (any agent reads any session)
- [ ] `Program.cs` — DI: choose SqliteSessionStore or PostgresSessionStore based on config
- [ ] Migration path: SQLite → Postgres (basic: export/import script, full migration tool later)
- [ ] Unit tests: PostgresSessionStore CRUD (using Testcontainers or mock)
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- task_069 (PostgreSQL shared state backend) — done (Npgsql already available)

## Scope / Likely files
src/agent/Storage/ISessionStore.cs (new), src/agent/Storage/SqliteSessionStore.cs (refactor), src/agent/Storage/PostgresSessionStore.cs (new), src/agent/Config/AppConfig.cs, src/agent/Program.cs

## Links
- Studio Stage 6: [../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md](../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md)
- Backlog: [../backlog.md](../backlog.md)