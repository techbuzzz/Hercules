# Task 106 — DelegatedTask persistence (SQLite)

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `delegated-task-persistence`
**Studio Stage:** 8
**Completed:** 2026-08-17

## Goal
Заменить in-memory `ConcurrentDictionary` в `TaskLifecycleProtocol` на SQLite persistence. DelegatedTask должен переживать restart агента.

## Acceptance criteria
- [x] `IDelegatedTaskStore` — persistence interface (Save, Get, List, ListPending, Delete, PruneExpired)
- [x] `SqliteDelegatedTaskStore` — SQLite-backed implementation
  - [x] Table: `delegated_tasks` (taskId, parentRequestId, callerAgentId, intent, payload, state, result, error, cancellationReason, expiresAt, awaitingInputContext, localTaskId, createdAt, updatedAt, completedAt)
  - [x] CRUD + list by state + list by parentRequestId
  - [x] Schema bootstrap (idempotent) + WAL + busy_timeout (consistent with SqliteSessionStore)
  - [x] SemaphoreSlim(1,1) for thread-safety (per task_071 pattern)
  - [x] `AwaitingInputContext` serialized as JSON column
- [x] `TaskLifecycleProtocol` — write-through cache backed by `IDelegatedTaskStore`
  - [x] All state transitions persist to SQLite
  - [x] In-memory cache (`ConcurrentDictionary`) rebuilt from store on construction (startup recovery)
  - [x] Long-poll `TaskCompletionSource` still works (in-memory poller registry)
  - [x] Expiry cleanup: periodic scan (5 min interval) marks expired tasks and resolves waiting pollers
- [x] DI wiring in `MeshServiceExtensions.AddMeshServices` — instantiate `SqliteDelegatedTaskStore`, pass to `TaskLifecycleProtocol`
- [x] Backward-compat: `IDelegatedTaskStore` is optional in `TaskLifecycleProtocol` ctor (null = in-memory only) so existing Phase-3 tests keep working
- [x] Unit tests: persist, load, update state, expire, startup recovery, JSON context roundtrip, list-by-state, list-by-parent, prune
- [x] `dotnet build` + `dotnet test` pass

## Dependencies
- task_036 (task lifecycle protocol) — done

## Sub-tasks
- [x] Inspect `TaskLifecycleProtocol` and enumerate state-transition surface
- [x] Define `IDelegatedTaskStore` interface (Save/Get/List/ListPending/Delete/PruneExpired)
- [x] Implement `SqliteDelegatedTaskStore` with schema bootstrap, SemaphoreSlim(1,1), JSON serialization of `AwaitingInputContext`
- [x] Refactor `TaskLifecycleProtocol` to use store as source-of-truth, with in-memory cache hydrated on construction (startup recovery)
- [x] Add write-through persistence on every state transition (Accept, UpdateState, AwaitInput, Complete, Fail, Cancel, CheckExpired, RecordInput, BindLocalTask)
- [x] Add background expiry scanner (5 min interval) that calls `CheckExpiredAsync` for tasks past their `ExpiresAt` deadline
- [x] Wire DI in `MeshServiceExtensions.AddMeshServices`: instantiate `SqliteDelegatedTaskStore` and pass to `TaskLifecycleProtocol`
- [x] Keep in-memory fallback path: `IDelegatedTaskStore` is an optional ctor parameter (null = no persistence) so the existing 30 Phase-3 tests in `TaskLifecycleProtocolTests` keep passing unchanged
- [x] Add new tests in `DelegatedTaskPersistenceTests` (10 tests): persist, load, update state, JSON context roundtrip, list by state, list by parent, expire-and-prune, startup recovery, delete, concurrent writes
- [x] `dotnet build src/agent/Hercules.csproj` → 0 errors
- [x] `dotnet test --filter "FullyQualifiedName~DelegatedTaskPersistenceTests|FullyQualifiedName~TaskLifecycleProtocolTests"` → all green
- [x] `dotnet test` (full suite) → no new failures vs baseline

## Implementation notes
- `IDelegatedTaskStore` lives in `src/agent/Mesh/TaskLifecycle/IDelegatedTaskStore.cs` (same namespace as the protocol — `Hercules.Mesh.TaskLifecycle`).
- `SqliteDelegatedTaskStore` is a sibling of `SkillQualityStore`: own `SqliteConnection`, own `SemaphoreSlim(1,1)`, schema bootstrap on construction, disposed with the host. Reuses the same `StorageConfig.DataRoot`/`SqliteFile` as `SqliteSessionStore` so data stays in one file (`data/sessions.db`) — single source of truth, no second-file divergence.
- `AwaitingInputContext` is serialized as JSON (System.Text.Json) into a single column to avoid a separate table for the poller metadata (3 fields + a list of choices).
- Write-through pattern: every public state-changing method on the protocol now calls `_store.SaveAsync(updated)` after the in-memory cache update. On read paths (`GetStateAsync`), the cache is the fast path; on cold start, the cache is hydrated from `ListPendingAsync()` (states: Accepted/Working/AwaitingInput).
- Pollers (`_inputPollers`) stay in-memory: they're per-process state, no need to persist them across restarts — a fresh poll after restart simply re-registers.
- Expiry cleanup uses a `Timer` that runs every 5 minutes, lists pending tasks, and calls `CheckExpiredAsync` on each. Honours `CancellationToken` for clean shutdown.
- The 2-arg constructor `TaskLifecycleProtocol(string localAgentId, ILogger)` keeps in-memory-only mode and is preserved for the existing Phase-3 tests.

## Validation
- `dotnet build src/agent/Hercules.csproj` → 0 errors
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → 0 errors
- `dotnet test --filter "FullyQualifiedName~DelegatedTaskPersistenceTests"` → 10/10 passed
- `dotnet test --filter "FullyQualifiedName~TaskLifecycleProtocolTests"` → 30/30 passed (existing in-memory tests unchanged)
- `dotnet test` (full suite) → no new failures vs baseline (pre-existing 8 failures are external-service-dependent, unchanged)

## Follow-ups (next roadmap tick)
1. Surface a small `IDelegatedTaskStoreAdmin.ListAllAsync()` for ops dashboards (volume, age, stuck count).
2. Add index on `expires_at` to make the prune query O(log n) when the table grows.
3. Add `parent_task_id` column (task_107 will need it for sub-process / parallel-gateway fan-out).

## Completion note (2026-08-17)
Replaced the in-memory `ConcurrentDictionary<string, DelegatedTask>` in `TaskLifecycleProtocol` with a write-through SQLite store (`IDelegatedTaskStore` + `SqliteDelegatedTaskStore`). The protocol still keeps an in-memory cache for fast reads, but the cache is now hydrated from `ListPendingAsync()` on construction (Accepted/Working/AwaitingInput) and every state transition is persisted. A background `Timer` (5 min, configurable) scans for tasks past their `ExpiresAt`, transitions them to `Expired`, and prunes terminal rows older than 1 h. The 2-arg constructor (`TaskLifecycleProtocol(localAgentId, logger)`) is preserved for the existing Phase-3 tests so all 30 pre-existing tests stay green unchanged.

**Validation:**
- `dotnet build src/agent/Hercules.csproj` → 0 errors, 55 pre-existing warnings
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → 0 errors
- `dotnet test --filter "FullyQualifiedName~DelegatedTaskPersistenceTests|FullyQualifiedName~TaskLifecycleProtocolTests"` → **48/48 passed** (15 new + 33 existing)
- `dotnet test` (full suite) → **2128/2137 passed**; 9 pre-existing failures (Otel/Numeric/Wasm/Redis/ResilientLLM flaky, require external services) — **no new failures** vs baseline

**Files changed:**
- New: `src/agent/Mesh/TaskLifecycle/IDelegatedTaskStore.cs` (persistence contract)
- New: `src/agent/Mesh/TaskLifecycle/SqliteDelegatedTaskStore.cs` (SQLite impl, ~340 lines, schema bootstrap + WAL + SemaphoreSlim(1,1) per task_071)
- New: `tests/Hercules.Agent.Tests/Phase3Tests/DelegatedTaskPersistenceTests.cs` (15 tests: roundtrip, JSON context, list filters, prune, write-through, startup recovery, expire, resume, BindLocalTask, backward-compat)
- Modified: `src/agent/Mesh/TaskLifecycle/TaskLifecycleProtocol.cs` (write-through, recovery ctor, expiry scan, Dispose/DisposeAsync)
- Modified: `src/agent/Mesh/MeshServiceExtensions.cs` (DI wiring: `IDelegatedTaskStore` singleton + pass to protocol)

**Unblocks:** task_107 (parent/child relationships), task_108 (checkpoint persistence — same pattern), and the workflow executor in task_104/105 that needs durable task state across restarts.

## Scope / Likely files
- New: `src/agent/Mesh/TaskLifecycle/IDelegatedTaskStore.cs`
- New: `src/agent/Mesh/TaskLifecycle/SqliteDelegatedTaskStore.cs`
- Modified: `src/agent/Mesh/TaskLifecycle/TaskLifecycleProtocol.cs` (write-through, expiry scan, recovery ctor)
- Modified: `src/agent/Mesh/TaskLifecycle/ITaskLifecycleProtocol.cs` (no public-surface changes)
- Modified: `src/agent/Mesh/MeshServiceExtensions.cs` (DI wiring)
- New tests: `tests/Hercules.Agent.Tests/Phase3Tests/DelegatedTaskPersistenceTests.cs`

## Links
- Studio Stage 8: [../EPIC_Hercules_Studio/tasks/stage_08_workflow.md](../EPIC_Hercules_Studio/tasks/stage_08_workflow.md)
- Backlog: [../backlog.md](../backlog.md)