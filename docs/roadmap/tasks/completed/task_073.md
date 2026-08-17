# Task 73 — Outbox synced-state and bounded-queue prune fix

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** —
**Slug:** `outbox-synced-fix`

## Goal
`SqliteOutboxStore.MarkSyncedAsync` (`Offline/SqliteOutboxStore.cs:139-143`) делает `DELETE FROM outbox_items WHERE item_id = $iid` вместо `UPDATE ... SET status='Synced'`. Из-за этого `PruneSyncedToCapAsync` (`:202-219`) и `GetSyncedCountAsync` (`:221-226`) всегда возвращают 0 — bounded-queue prune-логика мертва. `DeleteOlderThanAsync` (`:198`) удаляет по `created_at < cutoff` независимо от статуса, удаляя in-flight Pending items. Цель — корректно хранить Synced-записи для cap-pruning и TTL-retention.

## Acceptance criteria
### Sub-tasks
- [x] `Offline/SqliteOutboxStore.cs:139-143` — изменить `MarkSyncedAsync` на `UPDATE outbox_items SET status='Synced', synced_at=@now WHERE item_id=@iid` (не DELETE).
- [x] `Offline/SqliteOutboxStore.cs:198` — изменить `DeleteOlderThanAsync` на `DELETE FROM outbox_items WHERE created_at < @cutoff AND status IN ('Synced','Failed')` (не удалять Pending).
- [x] Добавить колонку `synced_at TEXT NULL` + индекс `CREATE INDEX IF NOT EXISTS idx_outbox_status_synced ON outbox_items(status, synced_at)`.
- [x] `Offline/SqliteOutboxStore.cs:62-81` — `EnqueueAsync` prune-логика теперь работает: когда `GetSyncedCountAsync` > `PruneSyncedThreshold`, вызывать `PruneSyncedToCapAsync` (удалять oldest Synced до `PruneSyncedKeep`).
- [x] `Offline/OfflineSyncService.cs:104-112` — добавить backoff между retry одного item: `Task.Delay(BackoffBase * 2^retryCount, ct)` перед следующей попыткой; max backoff 5 мин.
- [x] `Offline/OfflineSyncService.cs:158-163` — исправить closure: `OnReconnected` handler должен использовать свежий `CancellationTokenSource`, не captured `stoppingToken` (который cancelled после shutdown).
- [x] `Offline/OfflineSyncService.cs:246-256` — вынести hardcoded `Deadline = UtcNow.AddMinutes(5)` в config `OfflineSyncConfig.DefaultDeadlineMinutes`.
- [x] `Offline/SyncItem.cs:46` — убрать double ULID generation (factory methods уже генерируют; убрать default initializer).
- [x] Unit-тест: `MarkSyncedAsync` → row остаётся со status='Synced'; `GetSyncedCountAsync` возвращает 1.
- [x] Unit-тест: `EnqueueAsync` при достижении cap вызывает prune; `GetSyncedCountAsync` после prune <= `PruneSyncedKeep`.
- [x] Unit-тест: `DeleteOlderThanAsync` не удаляет Pending items.
- [x] `dotnet build` + `dotnet test` pass.

## Completion note
`SqliteOutboxStore` теперь корректно хранит `Synced`-записи (UPDATE вместо DELETE),
что вернуло к жизни `PruneSyncedToCapAsync` и `GetSyncedCountAsync` — bounded-queue
prune-логика больше не мертва. Добавлены `synced_at` колонка + индекс
`idx_outbox_status_synced`; `DeleteOlderThanAsync` сохраняет `Pending`-строки от
TTL-вытеснения. `OfflineSyncConfig` получил `PruneSyncedThreshold/PruneSyncedKeep`,
`DefaultDeadlineMinutes`, `MaxBackoffMs`. В `OfflineSyncService` появился
exponential backoff между retry одного item (cap 5 мин), `OnReconnected` теперь
использует свежий `CancellationTokenSource` через linked CTS, что корректно
обрабатывает shutdown. Убран default-инициализатор `OutboxItem.ItemId`,
исключающий двойную генерацию ULID.

Validation: `dotnet build src/agent/Hercules.csproj` (0 errors), `dotnet build
src/agent/Hercules.WebApi/Hercules.WebApi.csproj` (0 errors), `dotnet test
--filter FullyQualifiedName~Offline` (26 passed, 0 failed). Pre-existing failures
(OtelService, NumericValidator, WasmTool, RedisTaskQueue, BudgetGuard) подтверждены
на baseline (`git stash`) — к данной задаче не относятся.

## Implementation notes
- Добавлена колонка `synced_at TEXT` (UTC ISO-8601) — NULL для Pending/Failed; выставляется в `MarkSyncedAsync`.
- Новый индекс `idx_outbox_status_synced` ускоряет выборки `WHERE status='Synced' ORDER BY synced_at` для prune.
- `PruneSyncedKeep` (default 500) и `PruneSyncedThreshold` (default 500) добавлены в `OfflineSyncConfig`.
- `RetryBaseDelayMs` (default 1000) + `MaxBackoffMs` (default 300_000 = 5 min) — exponential backoff `Base * 2^retryCount` с cap.
- `OnReconnected` handler создаёт fresh `CancellationTokenSource`, отменяемый при shutdown.
- `DefaultDeadlineMinutes` (default 5) перенесён из hardcoded в конфиг.

## Scope / Likely files
src/agent/Offline/SqliteOutboxStore.cs, src/agent/Offline/OfflineSyncService.cs, src/agent/Offline/OfflineSyncConfig.cs, src/agent/Offline/SyncItem.cs

## Dependencies
- блокирует / опирается на: [task_060 — offline-resilience](task_060.md)

## Risks / Rollback
Хранение Synced-записей увеличивает размер таблицы до prune; убедиться что `PruneSyncedKeep` (default 500) достаточно мал. Rollback: git revert (вернуть DELETE).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)