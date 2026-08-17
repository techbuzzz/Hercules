# Task 108 — DurableTask checkpoint persistence

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `checkpoint-persistence`
**Studio Stage:** 8

## Goal
Заменить stub в `TaskExecutionService.CreateCheckpointAsync` на реальную persistence. Checkpoints должны сохраняться и загружаться.

## Acceptance criteria
- [x] `ITaskExecutionService`:
  - `CreateCheckpointAsync(taskId, stateSnapshot)` → save to SQLite
  - `ListCheckpointsAsync(taskId)` → return checkpoints from SQLite (currently returns empty)
  - `ResumeAsync(taskId, checkpointId?)` → load checkpoint state, resume from step
- [x] SQLite schema: `task_checkpoints` table (id, taskId, stepNumber, stateSnapshot JSON, createdAt)
- [x] `SqliteSessionStore` — add checkpoint CRUD methods
- [x] `TaskExecutionService`:
  - `CreateCheckpointAsync` — actually persist (was stub)
  - `ListCheckpointsAsync` — actually query (was returning empty)
  - `IncrementStepAsync` — persist step number
- [x] Startup recovery: load incomplete tasks → resume from last checkpoint
- [ ] Workflow executor integration: checkpoint after each node completion (deferred — depends on task_105)
- [x] Unit tests: create checkpoint, list, resume from checkpoint, recovery
- [x] `dotnet build` + `dotnet test` pass

## Sub-tasks
- [x] Add `LoadCheckpointAsync(string checkpointId)` to `ISessionStore` + `SqliteSessionStore` (and PostgresSessionStore stubs)
- [x] Inject `ISessionStore` into `TaskExecutionService` (alongside `ITaskRepository`)
- [x] Wire `CreateCheckpointAsync` to call `ISessionStore.SaveCheckpointAsync`
- [x] Wire `ListCheckpointsAsync` to call `ISessionStore.ListCheckpointsAsync`
- [x] Add `IncrementStepAsync` to `ITaskExecutionService` interface (was `internal` helper)
- [x] Update `ResumeAsync` to load the latest/specific checkpoint and log the snapshot step
- [x] Add `RecoverIncompleteTasksAsync` to `ITaskExecutionService` (startup recovery)
- [x] Add `GET /api/tasks/{id}/checkpoints/{ckptId}` endpoint (load specific checkpoint)
- [x] Add `POST /api/tasks/{id}/checkpoints` endpoint (create checkpoint)
- [x] Update `Program.cs` to invoke `RecoverIncompleteTasksAsync` at startup (after DI warmup, before `app.Run()`)
- [x] Update `TaskProgressController` to declare `.WithTags("Tasks")` for consistency
- [x] Update existing `CreateCheckpointAsync_ReturnsCheckpointId` test to assert persistence
- [x] Update `ListCheckpointsAsync_ReturnsEmptyList_Stub` → `ListCheckpointsAsync_ReturnsPersistedCheckpoints`
- [x] Add new tests: `LoadCheckpointAsync_Nonexistent`, `IncrementStepAsync_Persists`, `ResumeAsync_LoadsCheckpointSnapshot`, `ResumeAsync_FallsBackToLatestCheckpoint_WhenIdMissing`, `RecoverIncompleteTasksAsync_ResumesRunningAndPaused`, `RecoverIncompleteTasksAsync_SkipsFailedTasks`
- [x] `dotnet build` (full solution) + `dotnet test` (full suite) green; pre-existing unrelated failures unaffected

## Implementation notes

### Что было готово до этого тика

- `SqliteSessionStore.InitCheckpointSchemaAsync` — создаёт таблицу `task_checkpoints` (id, task_id, step_number, state_snapshot, created_at) и индекс по `task_id`. Вызывается в конструкторе `SqliteSessionStore`.
- `SqliteSessionStore.SaveCheckpointAsync` — `INSERT OR REPLACE` с правильным `TaskId`/`stepNumber`/snapshot. Под капотом `_connLock` (task_071) для сериализации.
- `SqliteSessionStore.ListCheckpointsAsync(string taskId)` — `SELECT … WHERE task_id = $tid ORDER BY step_number ASC`.
- `SqliteSessionStore.CleanupOldCheckpointsAsync(int retentionDays)` — удаляет по `created_at < $cutoff`.
- `ISessionStore` уже объявлял все четыре метода (task_103).

### Что добавил task_108

1. **`ISessionStore.LoadCheckpointAsync(string checkpointId)`** — новый метод для выборки одного чекпоинта по ID (нужен для `ResumeAsync(checkpointId=…)`). Реализован в `SqliteSessionStore` (параметризованный `SELECT` по `id`); `PostgresSessionStore` бросает `NotImplementedException` как и остальные checkpoint-методы (task_103 follow-up).

2. **`TaskExecutionService` — рефакторинг поверх `ISessionStore`:**
   - Внедрён `ISessionStore` через конструктор.
   - `CreateCheckpointAsync` теперь дёргает `_sessionStore.SaveCheckpointAsync` (раньше был stub, возвращавший ID без записи).
   - `ListCheckpointsAsync` теперь читает из `_sessionStore.ListCheckpointsAsync` (раньше возвращал пустой список).
   - `IncrementStepAsync` поднят с `internal` helper до публичного метода интерфейса (логика `_repo.UpdateAsync` уже была корректной — просто стала доступна извне).
   - `LoadCheckpointAsync(string)` — публичный прокси к `_sessionStore.LoadCheckpointAsync`.
   - `ResumeAsync(taskId, checkpointId?)`:
     - Если `checkpointId` задан — пытается загрузить конкретный чекпоинт через `LoadCheckpointAsync`; при отсутствии логирует warning и фолбэчится на последний из `ListCheckpointsAsync`.
     - Если `checkpointId` не задан — берёт самый свежий чекпоинт из `ListCheckpointsAsync` (если есть).
     - Возвращает задачу в `Running`, обнуляет `Error`, логирует `[Task] Resumed … from checkpoint {id} (step={n}, snapshotBytes={b})`.

3. **`TaskExecutionService.RecoverIncompleteTasksAsync` — startup recovery:**
   - Сканирует `Running`, `Paused`, `Failed` через `ITaskRepository.ListAsync(statusFilter: …)`.
   - Для `Running`/`Paused`: ставит `Status = Running`, обновляет `UpdatedAt`, обнуляет `Error`. Логирует `[Recovery] Resumed {PreviousStatus} task {Id} (checkpoint={id|None}, step={n})`.
   - Для `Failed`: оставляет статус, логирует `[Recovery] Skipped failed task {Id} (checkpoint={id|None}) — manual intervention required`.
   - В конце — best-effort `CleanupOldCheckpointsAsync(_config.CheckpointRetentionDays)`. Ошибка retention-cleanup не блокирует старт.
   - Возвращает количество реактивированных задач.

4. **DI wiring** (в `src/agent/Program.cs` + `src/agent/Hercules.WebApi/Program.cs`):
   - `ITaskExecutionService` теперь резолвит `ISessionStore` через конструктор.
   - В `Hercules.WebApi/Program.cs` после инициализации MCP добавлен блок `try { var recovered = await taskExec.RecoverIncompleteTasksAsync(); … }` (под `if (!isBuildTime)`).

5. **Controller** (`TaskProgressController.cs`):
   - `POST /api/tasks/{id}/checkpoints` — новый endpoint, body `CreateCheckpointRequest { int StepNumber, string? StateSnapshot }`. Создаёт чекпоинт + дёргает `IncrementStepAsync`, возвращает 201 Created с Location и метаданными.
   - `GET /api/tasks/{id}/checkpoints/{ckptId}` — новый endpoint, возвращает полный snapshot.
   - Все 8 существующих эндпойнтов получили `.WithTags("Tasks")` для консистентности с task_110/Orval tags-split (когда дойдут руки до web-UI codegen, тег уже будет на месте).

6. **Tests** (`tests/Hercules.Agent.Tests/Tasks/TaskExecutionServiceTests.cs`):
   - 13 старых тестов остались (signature constructor изменён — `ISessionStore` теперь обязателен).
   - `CreateCheckpointAsync_ReturnsCheckpointId` → `CreateCheckpointAsync_PersistsCheckpoint` (теперь ассертит round-trip через `LoadCheckpointAsync`).
   - `ListCheckpointsAsync_ReturnsEmptyList_Stub` → `ListCheckpointsAsync_ReturnsPersistedCheckpoints` (теперь создаёт 2 чекпоинта и проверяет порядок).
   - **Новые тесты:** `LoadCheckpointAsync_Nonexistent`, `IncrementStepAsync_Persists`, `ResumeAsync_LoadsCheckpointSnapshot`, `ResumeAsync_FallsBackToLatestCheckpoint_WhenIdMissing`, `RecoverIncompleteTasksAsync_ResumesRunningAndPaused`, `RecoverIncompleteTasksAsync_SkipsFailedTasks`.

### Дизайн-решения

- **Семантика recovery для Paused**: решил, что `Paused` → `Running` на старте, чтобы будущий workflow-экзекутор (task_104/105) мог подобрать задачи. Альтернатива (оставить Paused) делает recovery бессмысленным — Paused значит "ничего не делать, ждать явной команды". Тест `RecoverIncompleteTasksAsync_ResumesRunningAndPaused` фиксирует это поведение.
- **Failed остаётся Failed**: автоматический retry из Failed опасен (может зациклиться). Оператор должен явно `POST /api/tasks/{id}/resume` для Failed.
- **Retention cleanup best-effort**: если `CleanupOldCheckpointsAsync` упадёт, recovery всё равно завершится успешно. Логируется warning.
- **Checkpoint-fallback на latest**: `ResumeAsync(id, "ghost")` (несуществующий ID) не падает — фолбэчится на последний реальный чекпоинт. Это удобно для UI, который может прислать устаревший ID.

### Что осталось за рамками

- Workflow executor integration (task_105): `TaskExecutionService` сейчас не дёргает `CreateCheckpointAsync` после каждого узла графа — нет workflow-экзекутора. Когда task_105 будет готов, executor должен вызывать `svc.CreateCheckpointAsync(taskId, step, snapshotJson)` между узлами.
- PostgresSessionStore checkpoint methods — `NotImplementedException` stubs (task_103 follow-up, уже отмечено). Для greenfield Postgres deployments нужно реализовать `CREATE TABLE`, `INSERT`, `SELECT`, `DELETE` на Npgsql.
- Periodic retention cleanup (вместо startup-only) — текущая очистка работает только при рестарте. Для долгоживущих инсталляций стоит повесить `IHostedService` с периодическим таймером.

## Validation
- `dotnet build src/agent/Hercules.csproj` → 0 errors
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → 0 errors
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` → 0 errors
- `dotnet test --filter "FullyQualifiedName~TaskExecutionServiceTests"` → 19/19 passed
- `dotnet test` (full suite) → 2135/2143 passed; 8 failures are pre-existing baseline (OtelServiceTests×5, NumericValidatorTests×2, RedisTaskQueueTests×1 — все требуют внешних сервисов / экспортёров, не связаны с этой задачей). Совпадает с baseline, документированным в task_103.

## Scope / Likely files
- `src/agent/Tasks/ITaskExecutionService.cs` — добавлены `LoadCheckpointAsync`, `IncrementStepAsync`, `RecoverIncompleteTasksAsync`
- `src/agent/Tasks/TaskExecutionService.cs` — полный рефакторинг поверх `ISessionStore`
- `src/agent/Storage/ISessionStore.cs` — добавлен `LoadCheckpointAsync`
- `src/agent/Storage/SqliteSessionStore.cs` — добавлена реализация `LoadCheckpointAsync`
- `src/agent/Storage/PostgresSessionStore.cs` — добавлен stub `LoadCheckpointAsync` (NotImplementedException)
- `src/agent/Hercules.WebApi/Program.cs` — DI registration + startup recovery call
- `src/agent/Program.cs` — DI registration
- `src/agent/Hercules.WebApi/Controllers/TaskProgressController.cs` — новые эндпойнты + `.WithTags("Tasks")`
- `tests/Hercules.Agent.Tests/Tasks/TaskExecutionServiceTests.cs` — обновлены 2 теста, добавлено 6 новых

## Links
- Backlog: [../backlog.md](../backlog.md)
- task_018 (durable task lifecycle): [completed/task_018.md](completed/task_018.md) — базовая инфраструктура
- task_103 (ISessionStore + PostgresSessionStore): [completed/task_103.md](completed/task_103.md) — интерфейс, который мы тут дополняем
- task_105 (Workflow graph + executor): [task_105.md](task_105.md) — потребитель checkpoint API (follow-up)
- task_107 (Parent/child task relationships): [task_107.md](task_107.md) — follow-up

## Dependencies
- task_106 (DelegatedTask persistence — same pattern)
- task_018 (durable task lifecycle) — done

## Scope / Likely files
src/agent/Tasks/TaskExecutionService.cs (refactor), src/agent/Tasks/ITaskExecutionService.cs, src/agent/Storage/SqliteSessionStore.cs (add table)

## Links
- Studio Stage 8: [../EPIC_Hercules_Studio/tasks/stage_08_workflow.md](../EPIC_Hercules_Studio/tasks/stage_08_workflow.md)
- Backlog: [../backlog.md](../backlog.md)