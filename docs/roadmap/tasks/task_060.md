# Task 60 — Offline resilience

**Phase:** 5
**Initiative:** 38
**Status:** done
**Owner:** —
**Slug:** `offline-resilience`

## Goal
Edge-агент буферизует sensor logs, task results и outgoing alerts через bounded local queues; возобновляет sync с deduplication и ordering при восстановлении связи.

## Acceptance criteria
- [x] OfflineSyncConfig: bounded queue size (max items), TTL per item type, flush interval
- [x] OutboxItem model: Id, Type (sensor_log|task_result|alert), Payload (JSON), CreatedAt, Priority, Status (pending|synced|failed), RetryCount, LastError
- [x] SqliteOutboxStore: persists items in `outbox_items` table; adds `IF_NOT_EXISTS` migration on startup
- [x] OfflineSyncService:
  - Enqueue(item): adds to SQLite, respects bounded queue cap (drops oldest synced items first)
  - FlushAsync(): drains pending items to MeshBus (or direct endpoint); marks synced; handles failures with RetryCount
  - NetworkMonitor: polls connectivity (HTTP HEAD to mesh endpoint); raises OnReconnected event
  - BackgroundHostedService: flushes on OnReconnected + periodic interval (OfflineSyncConfig.FlushIntervalSeconds)
  - Deduplication: skip items already synced (by item Id) on re-connect
  - Ordering: items sorted by CreatedAt ASC within same priority
- [x] Registration in Program.cs: AddSingleton<OfflineSyncService>, register NetworkMonitor
- [x] Unit tests: Enqueue respects cap, Flush marks synced, deduplication works, periodic flush fires

## Scope / Likely files
src/agent/Offline/

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует / опирается на: [task_018 — durable-task-lifecycle](task_018.md)

## Risks / Rollback
Buffer overflow; явные приоритеты и TTL.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)

## Implementation notes
- Implemented in `src/agent/Offline/`: `OfflineSyncConfig`, `SyncItem`, `IOutboxStore`, `SqliteOutboxStore`, `INetworkMonitor`, `NetworkMonitor`, `OfflineSyncService`
- SQLite co-located with `SqliteSessionStore` (`outbox_items` table, WAL mode)
- Items published to mesh bus topics: `offline/sensor-log`, `offline/task-result`, `offline/alert`
- Config section: `OfflineSync` in appsettings.json (enabled by default, max 1000 items, 30s flush interval, 15s network poll)
- TTL cleanup runs on startup; capped at 1440 min (sensor logs), 60 min (task results), 30 min (alerts)
- `OfflineSyncConfig` added to `AppConfig.OfflineSync`
- 19 unit tests in `tests/Hercules.Agent.Tests/Offline/OfflineSyncTests.cs` — all passing

## Validation
- `dotnet build src/agent/Hercules.csproj` — 0 errors
- `dotnet test tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj --filter "FullyQualifiedName~Offline"` — 19 passed
- Full suite: 1564 passed, 8 pre-existing failures (OtelService + BudgetGuard)
