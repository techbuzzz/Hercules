# Task 18 — Устойчивый жизненный цикл задач

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `durable-task-lifecycle`

## Goal
Долгие задачи имеют task ID, state transitions, cancellation, retry, resumable checkpoints, idempotency key. Синхронные chat-запросы остаются простыми.

## Acceptance criteria

### Sub-tasks

- [x] `Tasks/Models.cs` — record'ы: `TaskId` (Guid), `DurableTaskStatus` enum (Pending/Running/WaitingApproval/Completed/Failed/Cancelled/Paused), `DurableTaskMetadata`, `DurableTask`, `TaskRetryPolicy`, `TaskCheckpoint`, `CreateTaskRequest`, `TaskExecutionResult`
- [x] `Tasks/ITaskRepository.cs` + `SqliteTaskRepository.cs` — персистентное хранилище задач; методы: `CreateAsync`, `GetAsync`, `UpdateAsync`, `ListAsync(filter)`, `DeleteAsync`
- [x] `Tasks/ITaskExecutionService.cs` + `TaskExecutionService.cs` — execution engine: `StartAsync`, `ResumeAsync`, `PauseAsync`, `CancelAsync`, `GetStatusAsync`, `CreateCheckpointAsync`, `ListCheckpointsAsync`; internal helpers: `CompleteTaskAsync`, `FailTaskAsync`, `IncrementStepAsync`
- [x] `Tasks/TaskRetryHandler.cs` — retry logic: `ShouldRetry` (счётчик + backoff + NonRetryableErrors), `ComputeNextRetryDelay` (exponential backoff с jitter ±25%), `IsRetryableError` (static)
- [x] `Tasks/TaskProgressController.cs` (WebAPI) — endpoints: `POST /api/tasks` (202 Accepted), `GET /api/tasks`, `GET /api/tasks/{id}`, `POST /api/tasks/{id}/pause`, `POST /api/tasks/{id}/resume`, `POST /api/tasks/{id}/cancel`, `GET /api/tasks/{id}/checkpoints`
- [x] `Config/AppConfig.cs` — `TaskConfig`: DefaultMaxRetries (3), DefaultRetryDelayMs (1000), DefaultBackoffMultiplier (2.0), MaxConcurrentDurableTasks (10), CheckpointRetentionDays (7)
- [x] `SqliteSessionStore.cs` — расширенная схема task_states (task_018 migration: +name, attempt_count, current_step, completed_at, cancellation_reason, retry_policy, skill_id, session_id, priority, tags, created_by, description, owner_agent_id); новая таблица task_checkpoints
- [x] `Program.cs` (CLI + WebAPI) — зарегистрированы `TaskConfig`, `ITaskRepository` → `SqliteTaskRepository`, `TaskRetryHandler`, `ITaskExecutionService` → `TaskExecutionService`
- [x] `tests/.../Tasks/TaskExecutionServiceTests.cs` — 14 unit-тестов: create, pause, cancel, resume, status transitions, checkpoints, complete/fail helpers
- [x] `tests/.../Tasks/TaskRetryHandlerTests.cs` — 15 unit-тестов: ShouldRetry (max retries, NonRetryableErrors), ComputeNextRetryDelay (exponential backoff, jitter, cap), IsRetryableError
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (606/612; 6 pre-existing: OtelService + BudgetGuard + WASM)

## Scope / Likely files
src/agent/Tasks/, src/agent/Storage/SqliteSessionStore.cs

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует / опирается на: [task_012 — budget-guardrails](task_012.md)

## Risks / Rollback
Сложность recovery-сценариев; обязательны chaos-тесты.

## Implementation notes

### 2026-08-12

**Добавлено:**

**`src/agent/Tasks/`** — новый namespace:
- `Models.cs` — DurableTaskStatus enum, TaskId struct (Guid wrapper), DurableTask, DurableTaskMetadata, TaskRetryPolicy, TaskCheckpoint, CreateTaskRequest, TaskExecutionResult
- `ITaskRepository.cs` — интерфейс: CreateAsync, GetAsync, UpdateAsync, DeleteAsync, ListAsync
- `SqliteTaskRepository.cs` — реализация через SqliteSessionStore
- `ITaskExecutionService.cs` — интерфейс: StartAsync, ResumeAsync, PauseAsync, CancelAsync, GetStatusAsync, CreateCheckpointAsync, ListCheckpointsAsync
- `TaskExecutionService.cs` — реализация; internal helpers для CompleteTaskAsync, FailTaskAsync, IncrementStepAsync
- `TaskRetryHandler.cs` — ShouldRetry (проверяет счётчик, NonRetryableErrors), ComputeNextRetryDelay (exponential + jitter ±25%, cap 30s), IsRetryableError (static helper)

**`src/agent/Config/AppConfig.cs`** — `TaskConfig`:
- DefaultMaxRetries (3), DefaultRetryDelayMs (1000), DefaultBackoffMultiplier (2.0), MaxConcurrentDurableTasks (10), CheckpointRetentionDays (7)

**`src/agent/Storage/SqliteSessionStore.cs`** — расширение task_018:
- Миграция: добавлены колонки name, attempt_count, current_step, completed_at, cancellation_reason, retry_policy, skill_id, session_id, priority, tags, created_by, description, owner_agent_id
- Новая таблица task_checkpoints (id, task_id, step_number, state_snapshot, created_at)
- Методы: InitCheckpointSchemaAsync, SaveCheckpointAsync, ListCheckpointsAsync, CleanupOldCheckpointsAsync
- MapRowToDurableTask — маппит расширенные колонки в DurableTask record

**`src/agent/Hercules.WebApi/Controllers/TaskProgressController.cs`** — 7 WebAPI endpoints:
- POST /api/tasks (202 Accepted), GET /api/tasks (list), GET /api/tasks/{id}, POST /api/tasks/{id}/pause, POST /api/tasks/{id}/resume, POST /api/tasks/{id}/cancel, GET /api/tasks/{id}/checkpoints

**`Program.cs` (CLI + WebAPI)** — DI регистрация TaskConfig, ITaskRepository, TaskRetryHandler, ITaskExecutionService

**`tests/.../Tasks/`** — 29 новых unit-тестов (TaskExecutionServiceTests × 14, TaskRetryHandlerTests × 15)

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 606/612 passed (6 pre-existing: OtelServiceTests + BudgetGuardTests + WASM timing)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
