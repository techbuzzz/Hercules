# Task 36 — Протокол жизненного цикла задач

**Phase:** 3
**Status:** done
**Owner:** —
**Slug:** `task-lifecycle-protocol`

## Goal
Delegated задачи: accepted, working, awaiting-input, completed, failed, cancelled, expired; вызывающий может poll, subscribe или callback по возможностям transport.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/TaskLifecycle/DelegatedTaskState.cs` — enum: Accepted, Working, AwaitingInput, Completed, Failed, Cancelled, Expired
- [x] `src/agent/Mesh/TaskLifecycle/DelegatedTask.cs` — record: taskId, parentRequestId, callerAgentId, intent, state, createdAt, updatedAt, completedAt, result, error, expiresAt
- [x] `src/agent/Mesh/TaskLifecycle/ITaskLifecycleProtocol.cs` — интерфейс: AcceptAsync, UpdateStateAsync, GetStateAsync, CheckExpiredAsync, CompleteAsync, FailAsync, CancelAsync
- [x] `src/agent/Mesh/TaskLifecycle/TaskLifecycleProtocol.cs` — реализация: связывает local DurableTask с inter-agent state machine
- [x] `src/agent/Mesh/TaskLifecycle/TaskLifecycleServiceExtensions.cs` — DI-регистрация
- [x] WebAPI endpoints в MeshController:
  - [x] `POST /api/mesh/tasks/accept` — принять delegated задачу
  - [x] `GET /api/mesh/tasks/{id}` — получить состояние delegated задачи
  - [x] `POST /api/mesh/tasks/{id}/state` — обновить state (Working/AwaitingInput)
  - [x] `POST /api/mesh/tasks/{id}/await-input` — перевести в AwaitingInput
  - [x] `POST /api/mesh/tasks/{id}/complete` — завершить с результатом
  - [x] `POST /api/mesh/tasks/{id}/fail` — завершить с ошибкой
  - [x] `POST /api/mesh/tasks/{id}/cancel` — отменить
  - [x] `POST /api/mesh/tasks/{id}/callback` — callback от вызывающего агента (input delivered)
  - [x] `GET /api/mesh/tasks/{id}/poll` — long-poll на AwaitingInput
  - [x] `POST /api/mesh/tasks/{id}/expire-check` — принудительная проверка expiration
  - [x] `POST /api/mesh/tasks/{id}/notify` — callback вызывающему агенту
- [x] `tests/Phase3Tests/TaskLifecycleProtocolTests.cs` — 33 unit tests: state transitions, expiration, invalid transitions, poll
- [x] `dotnet build` — 0 errors (warnings pre-existing)
- [x] `dotnet test` — Phase 3 tests pass (114/114)

## Scope / Likely files
src/agent/Mesh/TaskLifecycle/

## Dependencies
- блокирует / опирается на: [task_018 — durable-task-lifecycle](task_018.md)
- блокирует / опирается на: [task_035 — delegation-envelope](task_035.md)

## Risks / Rollback
Согласованность состояния при сбоях; idempotent transitions.

## Implementation notes

### 2026-08-13

**Добавлено:**

**`src/agent/Mesh/TaskLifecycle/`** — новый namespace для протокола:

- `DelegatedTaskState.cs` — enum: Accepted, Working, AwaitingInput, Completed, Failed, Cancelled, Expired
- `DelegatedTask.cs` — record для delegated задачи: taskId, parentRequestId, callerAgentId, intent, payload, state, timestamps, result, error, cancellationReason, expiresAt, AwaitingInputContext, localTaskId; вложенный `AwaitingInputContext` record для хранения контекста ожидания ввода
- `ITaskLifecycleProtocol.cs` — интерфейс: AcceptAsync, UpdateStateAsync, AwaitInputAsync, GetStateAsync, CompleteAsync, FailAsync, CancelAsync, CheckExpiredAsync, RecordInputAsync, BindLocalTaskAsync, PollForInputDeliveryAsync, NotifyStateChangeAsync
- `TaskLifecycleProtocol.cs` — реализация: ConcurrentDictionary in-memory хранилище, полный state machine с валидацией переходов, TaskCompletionSource для long-poll на AwaitingInput, callback через IntentTransport
- `TaskLifecycleServiceExtensions.cs` — DI-регистрация

**`src/agent/Mesh/MeshServiceExtensions.cs`** — добавлена регистрация `ITaskLifecycleProtocol` в `AddMeshServices()` с `IntentTransport` для callback-уведомлений.

**`src/agent/Hercules.WebApi/Controllers/MeshController.cs`** — 11 новых endpoints:
- POST `/api/mesh/tasks/accept` — принять delegated задачу
- GET `/api/mesh/tasks/{id}` — получить состояние
- POST `/api/mesh/tasks/{id}/state` — обновить state
- POST `/api/mesh/tasks/{id}/await-input` — перевести в AwaitingInput
- POST `/api/mesh/tasks/{id}/complete` — завершить с результатом
- POST `/api/mesh/tasks/{id}/fail` — завершить с ошибкой
- POST `/api/mesh/tasks/{id}/cancel` — отменить
- POST `/api/mesh/tasks/{id}/callback` — callback от вызывающего агента
- GET `/api/mesh/tasks/{id}/poll` — long-poll на AwaitingInput
- POST `/api/mesh/tasks/{id}/expire-check` — проверка expiration
- POST `/api/mesh/tasks/{id}/notify` — callback вызывающему агенту

**`tests/.../Phase3Tests/TaskLifecycleProtocolTests.cs`** — 33 unit tests:
- accept, deadline, state transitions, invalid transitions, terminal state blocking, expiration, record input, bind local task, poll resolution, AwaitingInput context scenarios

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 14 warnings (pre-existing: NU1902 Otel, CS8602/CS8620/CS0618)
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 13 warnings (pre-existing)
- `dotnet build` Hercules.Agent.Tests.csproj — 0 errors, 57 warnings (pre-existing)
- `dotnet test --filter Phase3` — 114/114 passed
- `dotnet test --filter TaskLifecycleProtocolTests` — 33/33 passed
- Full suite — 957/963 (6 pre-existing: OtelService + BudgetGuard)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
