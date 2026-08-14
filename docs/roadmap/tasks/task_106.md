# Task 106 — DelegatedTask persistence (SQLite)

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `delegated-task-persistence`
**Studio Stage:** 8

## Goal
Заменить in-memory `ConcurrentDictionary` в `TaskLifecycleProtocol` на SQLite persistence. DelegatedTask должен переживать restart агента.

## Acceptance criteria
- [ ] `ITaskLifecycleProtocol` — persistence interface methods (Save, Get, List, Update, Delete)
- [ ] `SqliteDelegatedTaskStore` — SQLite-backed implementation
  - Table: `delegated_tasks` (taskId, parentRequestId, callerAgentId, intent, payload, state, result, error, cancellationReason, expiresAt, awaitingInputContext, localTaskId, createdAt, updatedAt)
  - CRUD + list by state + list by parent
- [ ] `TaskLifecycleProtocol` — use store instead of in-memory dict
  - All state transitions persist to SQLite
  - Long-poll `TaskCompletionSource` still works (rebuild on startup from pending tasks)
- [ ] Startup recovery: load pending tasks (Accepted/Working/AwaitingInput) → resume
- [ ] Expiry cleanup: background timer, delete expired tasks
- [ ] Unit tests: persist, load, update state, expire, startup recovery
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- task_036 (task lifecycle protocol) — done

## Scope / Likely files
src/agent/Mesh/TaskLifecycle/TaskLifecycleProtocol.cs (refactor), src/agent/Mesh/TaskLifecycle/SqliteDelegatedTaskStore.cs (new), src/agent/Storage/SqliteSessionStore.cs (add table)

## Links
- Studio Stage 8: [../EPIC_Hercules_Studio/tasks/stage_08_workflow.md](../EPIC_Hercules_Studio/tasks/stage_08_workflow.md)
- Backlog: [../backlog.md](../backlog.md)