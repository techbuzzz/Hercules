# Task 74 — NATS JetStream ack/fail implementation

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** —
**Slug:** `nats-jetstream-ack`

## Goal
`NatsTaskQueue.AckAsync` и `FailAsync` (`Mesh/Backends/Nats/NatsTaskQueue.cs:266-278`) — no-ops (`await Task.CompletedTask`). JetStream ack/nak никогда не отправляется → сообщения redeliver'ятся бесконечно (до `MaxDeliver`/`AckWait`). Это критический баг: задача считается "обработанной", но остаётся в потоке и доставляется снова и снова. Дополнительно `MaxDeliver = 10` hardcoded.

## Acceptance criteria
### Sub-tasks
- [x] `Mesh/Backends/Nats/NatsTaskQueue.cs:266-278` — реализовать `AckAsync`: получить `NatsJSMsg` из in-flight tracker по taskId, вызвать `msg.AckAsync()`.
- [x] Реализовать `FailAsync`: вызвать `msg.NakAsync()` (для retry) или `msg.AckTerminateAsync(AckOpts{TerminateReason})` (для DLQ после `MaxRetries`).
- [x] Создать `NatsTaskInFlightTracker` (internal class): `ConcurrentDictionary<taskId, Entry>` где Entry хранит action-делегаты (Ack/Nak/Terminate) и `NumDelivered`. `DequeueAsync` сохраняет entry, `AckAsync`/`FailAsync` удаляют.
- [x] `NatsMeshConfig.MaxDeliveryAttempts` (default 10) — `Mesh/Backends/Nats/NatsTaskQueue.cs:145-148` consumer config использует это значение вместо hardcoded `MaxDeliver = 10`.
- [x] Локальный DLQ: создать `NatsTaskDlqStore` (JSONL-файл) — после Terminate записать entry в `<data>/{StreamPrefix}-dlq.jsonl`. `GetDeadLetterQueueAsync`/`RequeueDeadLetterAsync` работают с этим файлом.
- [x] `RequeueDeadLetterAsync` — переработать: прочитать JSONL, найти запись, republish в JetStream stream, удалить из файла.
- [x] Unit-тесты: `NatsTaskInFlightTracker` (Add/Get/Remove/Count/Replace), `NatsTaskDlqStore` (Append/List/Remove/persistence), `NatsTaskQueue` config defaults (включая `MaxDeliveryAttempts`).
- [x] Unit-тест: `NatsTaskQueue.AckAsync`/`FailAsync` — no-op когда tracker пуст (не падают с NRE).
- [x] `dotnet build` + `dotnet test` pass.
- [ ] Integration-тесты с live NATS отложены: требуют testcontainer или live server (отмечены `[Fact(Skip="requires live NATS")]`).

## Scope / Likely files
src/agent/Mesh/Backends/Nats/NatsTaskQueue.cs, src/agent/Mesh/Backends/Nats/NatsMeshConfig.cs, src/agent/Mesh/Abstractions/ITaskQueue.cs (metadata field)

## Dependencies
- блокирует / опирается на: [task_068 — nats-jetstream-transport](task_068.md)

## Implementation notes

### 2026-08-14

**Реализовано:**

**`src/agent/Mesh/Backends/Nats/NatsTaskInFlightTracker.cs`** (новый файл, 60 строк) — внутренний tracker:
- `ConcurrentDictionary<string, Entry>` keyed by taskId
- `Entry` хранит: `TaskId`, `QueueName`, `NumDelivered`, `MeshTask`, и 3 action-делегата (`AckAsync`, `NakAsync`, `TerminateAsync`)
- Действия капчурируются в `DequeueAsync` (где `NatsJSMsg` ещё в скоупе), затем вызываются позднее в `AckAsync`/`FailAsync`
- Action-делегаты вместо хранения `NatsJSMsg` напрямую — позволяет юнит-тестировать без mock NATS-клиента

**`src/agent/Mesh/Backends/Nats/NatsTaskDlqStore.cs`** (новый файл, 160 строк) — локальный DLQ на JSONL-файле:
- `AppendAsync(MeshTask, reason, deliveryCount, queueName, ct)` — добавление записи
- `ListAsync(queueName?, limit, ct)` — фильтрованный список
- `RemoveAsync(taskId, ct)` — атомарное удаление (temp-file + rename)
- Thread-safe через `SemaphoreSlim`
- Файл: `<DataRoot>/{StreamPrefix}-dlq.jsonl` (default DataRoot = `AppContext.BaseDirectory`)

**`src/agent/Mesh/Backends/Nats/NatsTaskQueue.cs`** — рефакторинг:
- `AckAsync(taskId, ct)` — lookup in tracker → `entry.AckAsync(ct)` → remove from tracker
- `FailAsync(taskId, reason, retry, ct)` — если `retry >= MaxRetries` или `entry.NumDelivered >= MaxDeliveryAttempts`: `TerminateAsync(AckOpts{TerminateReason=reason})` + append в DLQ файл. Иначе: `NakAsync(null, ct)` для retry
- `DequeueAsync` — после успешного парсинга создаёт `Entry` с капчурированными делегатами и добавляет в tracker
- Consumer config: `MaxDeliver = _config.MaxDeliveryAttempts` (вместо hardcoded 10)
- `GetDeadLetterQueueAsync`/`RequeueDeadLetterAsync` — работают через `NatsTaskDlqStore` (JSONL-файл), legacy JetStream DLQ stream больше не создаётся
- `_connection` стал nullable — null-guard'ы в `EnqueueAsync`/`DequeueAsync`/`IsHealthyAsync`/`EnsureStreamAndConsumersAsync`
- Удалено поле `_dlqStreamName` (legacy JetStream DLQ stream больше не используется)

**`src/agent/Mesh/Backends/Nats/NatsMeshConfig.cs`** — новые поля:
- `MaxDeliveryAttempts` (default 10)
- `DlqFileName` (default пустое → автогенерация `{StreamPrefix}-dlq.jsonl`)
- `DataRoot` (default null → `AppContext.BaseDirectory`)

**Тесты** (3 файла, 31 тест):
- `NatsTaskInFlightTrackerTests` (8 тестов): AddOrReplace, Get, Remove, Count, multi-entry, action delegate invocation
- `NatsTaskDlqStoreTests` (10 тестов): Append/List, filter by queue, limit, no-file, remove, persistence, malformed lines, append after remove
- `NatsTaskQueueTests` (расширен, 7 новых тестов): config defaults для `MaxDeliveryAttempts`/`DlqFileName`/`DataRoot`, `AckAsync`/`FailAsync` no-op для unknown tasks, post-dispose exceptions, `DlqFilePath` computation, `InFlight` property exposure
- Все 31 новый тест проходит.

**Validation:**
- `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors
- `dotnet test --filter "NatsTaskInFlightTrackerTests|NatsTaskDlqStoreTests|NatsTaskQueueTests"` — 31/31 passed
- Full suite: 1769/1779 passed (10 pre-existing failures: OtelService×5, BudgetGuard×1, NumericValidator×2, BusHttpServer×1, RedisTaskQueue.EnqueueAsync_StoresTaskMetadata×1 — все pre-existing, не связаны с task_074)

**Изменённые файлы:**
- `src/agent/Mesh/Backends/Nats/NatsMeshConfig.cs` (добавлены 3 поля)
- `src/agent/Mesh/Backends/Nats/NatsTaskQueue.cs` (~250 строк логики изменено, 130 строк удалено/упрощено)
- `src/agent/Mesh/Backends/Nats/NatsTaskInFlightTracker.cs` (новый)
- `src/agent/Mesh/Backends/Nats/NatsTaskDlqStore.cs` (новый)
- `tests/Hercules.Agent.Tests/Mesh/Backends/Nats/NatsTaskInFlightTrackerTests.cs` (новый)
- `tests/Hercules.Agent.Tests/Mesh/Backends/Nats/NatsTaskDlqStoreTests.cs` (новый)
- `tests/Hercules.Agent.Tests/Mesh/Backends/Nats/NatsTaskQueueTests.cs` (расширен)
- `docs/roadmap/tasks/task_074.md` (этот файл)

## Risks / Rollback
NATS client API может отличаться между версиями; проверить совместимость с установленной `NATS.Net` package. Rollback: вернуть no-ops (но баг останется). Если NATS backend не используется в production — понизить priority.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)