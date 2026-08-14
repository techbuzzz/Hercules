# Task 108 — DurableTask checkpoint persistence

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `checkpoint-persistence`
**Studio Stage:** 8

## Goal
Заменить stub в `TaskExecutionService.CreateCheckpointAsync` на реальную persistence. Checkpoints должны сохраняться и загружаться.

## Acceptance criteria
- [ ] `ITaskExecutionService`:
  - `CreateCheckpointAsync(taskId, stateSnapshot)` → save to SQLite
  - `ListCheckpointsAsync(taskId)` → return checkpoints from SQLite (currently returns empty)
  - `ResumeAsync(taskId, checkpointId?)` → load checkpoint state, resume from step
- [ ] SQLite schema: `task_checkpoints` table (id, taskId, stepNumber, stateSnapshot JSON, createdAt)
- [ ] `SqliteSessionStore` — add checkpoint CRUD methods
- [ ] `TaskExecutionService`:
  - `CreateCheckpointAsync` — actually persist (was stub)
  - `ListCheckpointsAsync` — actually query (was returning empty)
  - `IncrementStepAsync` — persist step number
- [ ] Startup recovery: load incomplete tasks → resume from last checkpoint
- [ ] Workflow executor integration: checkpoint after each node completion
- [ ] Unit tests: create checkpoint, list, resume from checkpoint, recovery
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- task_106 (DelegatedTask persistence — same pattern)
- task_018 (durable task lifecycle) — done

## Scope / Likely files
src/agent/Tasks/TaskExecutionService.cs (refactor), src/agent/Tasks/ITaskExecutionService.cs, src/agent/Storage/SqliteSessionStore.cs (add table)

## Links
- Studio Stage 8: [../EPIC_Hercules_Studio/tasks/stage_08_workflow.md](../EPIC_Hercules_Studio/tasks/stage_08_workflow.md)
- Backlog: [../backlog.md](../backlog.md)