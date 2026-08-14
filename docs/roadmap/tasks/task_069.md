# Task 69 — PostgreSQL shared state backend

**Phase:** 4
**Initiative:** 29
**Status:** done
**Owner:** —
**Slug:** `postgres-shared-state`

## Goal
Опциональный PostgreSQL state store хранит cross-agent workflow state, shared skill registry, evaluation records и audit logs. Job-очереди используют `SELECT … FOR UPDATE SKIP LOCKED` для умеренно-throughput исполнения. Бэкенд активируется профилем, не обязателен.

## Acceptance criteria

### Sub-tasks

- [x] `Mesh/Backends/Postgres/PostgresMeshConfig.cs` — config: Enabled, ConnectionString, Schema, KeyPrefix, ChannelPrefix, DefaultTtlSeconds, DefaultVisibilityTimeoutSec, UseListenNotify, WatchPollingIntervalMs, ConnectTimeoutMs
- [x] `Mesh/Backends/Postgres/PostgresMeshStateStore.cs` — IMeshStateStore: Get/Set/CompareAndSet/Delete/Exists/Increment/Watch/ScanKeys/IsHealthy using `hercules_mesh_state` table (JSONB data, version, created/updated/expires_at, last_writer_agent_id) with auto-create on first use
- [x] `Mesh/Backends/Postgres/PostgresTaskQueue.cs` — ITaskQueue: Enqueue/Dequeue/Ack/Fail/DLQ/RequeueDeadLetter/IsHealthy using `hercules_mesh_tasks` table with `SELECT ... FOR UPDATE SKIP LOCKED` for at-least-once delivery and visibility timeout
- [x] `Mesh/Backends/Postgres/PostgresMeshBus.cs` — IMeshBus: Publish/Subscribe/RequestReply/IsHealthy using `LISTEN/NOTIFY` on `hercules_bus_{topic}` channels; correlation ID for request/reply
- [x] `Config/AppConfig.cs` — add `Postgres` property of type `PostgresMeshConfig`
- [x] `Mesh/MeshServiceExtensions.cs` — extend `RegisterMeshBackends` to wire Postgres when `appConfig.Postgres.Enabled` or `MeshBackendProfile.Postgres`
- [x] `Hercules.csproj` — add `Npgsql 8.0.x` package
- [x] Unit tests: `tests/.../Mesh/Backends/PostgresMeshConfigTests.cs`, `PostgresMeshStateStoreTests.cs`, `PostgresTaskQueueTests.cs`, `PostgresMeshBusTests.cs` (BackendKind, IsHealthy on unreachable host, Dispose, config defaults)
- [x] `dotnet build` + `dotnet test` pass

## Scope / Likely files
src/agent/Mesh/Backends/Postgres/PostgresMeshConfig.cs, src/agent/Mesh/Backends/Postgres/PostgresMeshStateStore.cs, src/agent/Mesh/Backends/Postgres/PostgresTaskQueue.cs, src/agent/Mesh/Backends/Postgres/PostgresMeshBus.cs

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md)
- блокирует / опирается на: [task_070 — backend-profiles-degradation](task_070.md)

## Implementation notes

### 2026-08-14

**`Mesh/Backends/Postgres/PostgresMeshConfig.cs`** — 14 properties:
Enabled, ConnectionString (Npgsql format), Schema ("hercules_mesh"), StateTable ("state"),
TasksTable ("tasks"), DlqTable ("tasks_dlq"), ChannelPrefix ("hercules_bus_"),
DefaultTtlSeconds (3600), DefaultVisibilityTimeoutSec (30), MaxDeliveryAttempts (3),
UseListenNotify (true), WatchPollingIntervalMs (500), ConnectTimeoutSeconds (5),
AutoCreateSchema (true), RequeueTimerIntervalMs (1000).

**`Mesh/Backends/Postgres/PostgresMeshStateStore.cs`** — `IMeshStateStore` over PostgreSQL JSONB:
- `hercules_mesh.state` table: `key TEXT PK, data JSONB, version TEXT, created_at, updated_at, expires_at, last_writer_agent_id`
- `GetAsync`: SELECT with TTL filter
- `SetAsync`: UPSERT preserving created_at, generating new version
- `CompareAndSetAsync`: full CAS with expected version + create-if-not-exists (expectedVersion=null)
- `IncrementAsync`: UPSERT using `jsonb_set` to atomically increment `{value}` counter
- `WatchAsync`: LISTEN/NOTIFY when UseListenNotify=true, polling fallback otherwise; prefix watchers
- `ScanKeysAsync`: SELECT with LIKE + ESCAPE for prefix enumeration
- `IsHealthyAsync`: `SELECT 1` round-trip
- Auto-creates schema + table on first use; identifier quoting escapes embedded double quotes
- Schema init is single-attempt with reset on failure

**`Mesh/Backends/Postgres/PostgresTaskQueue.cs`** — `ITaskQueue` with `SELECT FOR UPDATE SKIP LOCKED`:
- `hercules_mesh.tasks` table: `id TEXT PK, queue_name, status (queued|in_flight), payload JSONB, enqueued_at, visibility_expires_at, not_before, receipt_handle, delivery_count, retry_count, max_retries`
- `hercules_mesh.tasks_dlq` table: same payload + failed_at, retry_count, failure_reason
- `DequeueAsync`: CTE picks one row with FOR UPDATE SKIP LOCKED, sets status='in_flight', visibility_expires_at, receipt_handle; committed transaction releases the lock
- `EnqueueAsync`: INSERT with ON CONFLICT DO NOTHING (idempotent on retries)
- `FailAsync`: locks row, requeues with retry budget or moves to DLQ
- `RequeueDeadLetterAsync`: re-inserts task to main queue from DLQ
- `IsHealthyAsync`: `SELECT 1` round-trip
- Background `Timer` (1s default) requeues timed-out in_flight tasks (visibility_expires_at < NOW)

**`Mesh/Backends/Postgres/PostgresMeshBus.cs`** — `IMeshBus` over LISTEN/NOTIFY:
- `hercules_mesh.messages` table: `channel TEXT PK, payload JSONB, sent_at` (side-store for late subscribers to read after notification)
- `PublishAsync`: UPSERT payload to `messages` then `pg_notify` on the same channel name
- `SubscribeAsync`: LISTEN on the topic channel; background Wait loop dispatches notifications to local subscribers (in-process fan-out for co-hosted agents)
- `RequestReplyAsync`: separate `reply_{correlationId}` channel with TaskCompletionSource; correlation ID = envelope.RequestId (auto-generated if empty)
- Reference-counted listen connections (multi-subscribe = single LISTEN)
- `CleanupStaleReplies` timer (10s) prunes TCS that completed >5 min ago

**`Config/AppConfig.cs`** — added `Postgres` property of type `PostgresMeshConfig`.

**`Mesh/MeshServiceExtensions.cs`** — extended `RegisterMeshBackends` to add the Postgres branch:
- Triggered when `appConfig.Postgres.Enabled` or `MeshBackendProfile.Postgres` is the active profile
- Registers `NpgsqlDataSource` (singleton) via `NpgsqlDataSourceBuilder`
- Wires `IMeshBus → PostgresMeshBus`, `ITaskQueue → PostgresTaskQueue`, `IMeshStateStore → PostgresMeshStateStore`

**`Hercules.csproj`** — added `Npgsql 8.0.5` package.

**Tests** (`tests/Hercules.Agent.Tests/Mesh/Backends/`):
- `PostgresMeshConfigTests.cs` — 2 tests: default values, full property set
- `PostgresMeshStateStoreTests.cs` — 8 tests: BackendKind, null-arg validation (3×), IsHealthy unreachable, GetAsync unreachable throws, GetAsync null/empty key, identifier quoting, Dispose idempotency, DisposedStore throws
- `PostgresTaskQueueTests.cs` — 8 tests: BackendKind, null-arg validation (3×), IsHealthy unreachable, EnqueueAsync null task, EnqueueAsync unreachable throws, Dispose idempotency, DisposedQueue throws
- `PostgresMeshBusTests.cs` — 7 tests: BackendKind, null-arg validation (3×), IsHealthy unreachable, Dispose idempotency, DisposedBus throws
- Happy paths require a live PostgreSQL instance and are documented as integration-suite scope

**Validation:**
- `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors (pre-existing OpenTelemetry/Cache warnings unchanged)
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj -c Release` — 0 errors
- `dotnet test --filter "FullyQualifiedName~Postgres" -c Release` — 28/28 passed
- Full suite: 1724/1733 (9 pre-existing failures unchanged: OtelService×5, BudgetGuard×1, NumericValidator×2, BusHttpServer×1)

## Risks / Rollback
Operational overhead БД; миграции, бэкапы, connection pooling; чёткие runbooks.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
