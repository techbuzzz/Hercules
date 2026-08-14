# Task 73 — Outbox synced-state and bounded-queue prune fix

**Phase:** 5
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `outbox-synced-fix`

## Goal
`SqliteOutboxStore.MarkSyncedAsync` (`Offline/SqliteOutboxStore.cs:139-143`) делает `DELETE FROM outbox_items WHERE item_id = $iid` вместо `UPDATE ... SET status='Synced'`. Из-за этого `PruneSyncedToCapAsync` (`:202-219`) и `GetSyncedCountAsync` (`:221-226`) всегда возвращают 0 — bounded-queue prune-логика мертва. `DeleteOlderThanAsync` (`:198`) удаляет по `created_at < cutoff` независимо от статуса, удаляя in-flight Pending items. Цель — корректно хранить Synced-записи для cap-pruning и TTL-retention.

## Acceptance criteria
### Sub-tasks
- [ ] `Offline/SqliteOutboxStore.cs:139-143` — изменить `MarkSyncedAsync` на `UPDATE outbox_items SET status='Synced', synced_at=@now WHERE item_id=@iid` (не DELETE).
- [ ] `Offline/SqliteOutboxStore.cs:198` — изменить `DeleteOlderThanAsync` на `DELETE FROM outbox_items WHERE created_at < @cutoff AND status IN ('Synced','Failed')` (не удалять Pending).
- [ ] Добавить индекс `CREATE INDEX IF NOT EXISTS idx_outbox_status_synced ON outbox_items(status, synced_at)`.
- [ ] `Offline/SqliteOutboxStore.cs:62-81` — `EnqueueAsync` prune-логика теперь работает: когда `GetSyncedCountAsync` > `PruneSyncedThreshold`, вызывать `PruneSyncedToCapAsync` (удалять oldest Synced до `PruneSyncedKeep`).
- [ ] `Offline/OfflineSyncService.cs:104-112` — добавить backoff между retry одного item: `Task.Delay(BackoffBase * 2^retryCount, ct)` перед следующей попыткой; max backoff 5 мин.
- [ ] `Offline/OfflineSyncService.cs:158-163` — исправить closure: `OnReconnected` handler должен использовать свежий `CancellationTokenSource`, не captured `stoppingToken` (который cancelled после shutdown).
- [ ] `Offline/OfflineSyncService.cs:246-256` — вынести hardcoded `Deadline = UtcNow.AddMinutes(5)` в config `OfflineSyncConfig.DefaultDeadlineMinutes`.
- [ ] `Offline/SyncItem.cs:46` — убрать double ULID generation (factory methods уже генерируют; убрать default initializer).
- [ ] Unit-тест: `MarkSyncedAsync` → row остаётся со status='Synced'; `GetSyncedCountAsync` возвращает 1.
- [ ] Unit-тест: `EnqueueAsync` при достижении cap вызывает prune; `GetSyncedCountAsync` после prune <= `PruneSyncedKeep`.
- [ ] Unit-тест: `DeleteOlderThanAsync` не удаляет Pending items.
- [ ] `dotnet build` + `dotnet test` pass.

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