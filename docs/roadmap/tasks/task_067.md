# Task 67 — Redis/Valkey coordination backend

**Phase:** 4
**Initiative:** 27
**Status:** done
**Owner:** —
**Slug:** `redis-coordination-backend`

## Goal
Опциональный RESP-совместимый in-memory бэкенд (Redis или Valkey) даёт working memory, distributed locks и эфемерные очереди для координации при высокой concurrency. Durable truth остаётся в SQLite/PostgreSQL. Бэкенд активируется профилем развёртывания, не обязан быть запущен.

## Acceptance criteria

### Sub-tasks

- [x] `Redis/RedisMeshConfig.cs` — config: Enabled, ConnectionString, KeyPrefix, ChannelPrefix, DefaultTtlSeconds, DefaultVisibilityTimeoutSec, UseKeyspaceNotifications, WatchPollingIntervalMs
- [x] `Redis/RedisMeshBus.cs` — IMeshBus: PublishAsync (Redis pub/sub), SubscribeAsync (local fan-out), RequestReplyAsync (correlation ID + TCS), IsHealthyAsync (PING)
- [x] `Redis/RedisMeshStateStore.cs` — IMeshStateStore: Get/Set/CAS/Delete/Exists/Increment/Scan/Watch using Redis HASH, TTL, WATCH, INCR, polling for watch
- [x] `Redis/RedisTaskQueue.cs` — ITaskQueue: EnqueueAsync (RPUSH), DequeueAsync (ListLeftPop), AckAsync/FailAsync, DLQ, visibility timeout via sorted set + background requeue
- [x] `Config/AppConfig.cs` — add `Redis` property of type `RedisMeshConfig`
- [x] `MeshServiceExtensions.cs` — conditionally register Redis backends when `appConfig.Redis.Enabled` or `MeshBackendProfile.Redis`
- [x] `dotnet build` succeeds
- [x] Unit tests: RedisMeshBus, RedisMeshStateStore, RedisTaskQueue unit tests

## Implementation notes

### 2026-08-14

**`src/agent/Mesh/Backends/Redis/RedisMeshConfig.cs`** — 9 properties: Enabled, ConnectionString, KeyPrefix, ChannelPrefix, DefaultTtlSeconds, DefaultVisibilityTimeoutSec, UseKeyspaceNotifications, WatchPollingIntervalMs.

**`src/agent/Mesh/Backends/Redis/RedisMeshBus.cs`** — RESP pub/sub bus:
- `PublishAsync`: Redis PUBLISH to `ChannelPrefix + topic`
- `SubscribeAsync`: local fan-out with `ConcurrentDictionary` + `Channel<>` per topic (hybrid local + Redis)
- `RequestReplyAsync`: correlation ID TCS + PUBLISH to intent/agent topics; `RouteReply` completes TCS on response
- `IsHealthyAsync`: PING Redis
- Graceful fallback to local subscribers on `RedisConnectionException`
- Background task for Redis channel subscription

**`src/agent/Mesh/Backends/Redis/RedisMeshStateStore.cs`** — distributed KV store:
- `GetAsync`: Redis HASH GET ALL, TTL check, expiry cleanup
- `SetAsync`: Redis HASH SET with auto-generated version, CreatedAt preservation
- `CompareAndSetAsync`: optimistic locking with transaction; NX for create-if-not-exists
- `DeleteAsync/ExistsAsync`: Key operations
- `IncrementAsync`: StringIncrement with watcher notification
- `WatchAsync`: polling-based (configurable interval), exact key and prefix wildcard support
- `ScanKeysAsync`: Redis SCAN with MATCH pattern, key prefix stripping
- `IsHealthyAsync`: PING

**`src/agent/Mesh/Backends/Redis/RedisTaskQueue.cs`** — durable queue:
- `EnqueueAsync`: RPUSH receipt handle, store payload in Redis STRING with TTL
- `DequeueAsync`: ListLeftPop with timeout, track in-flight via sorted set (score = expiry timestamp)
- `AckAsync`: scan in-flight sets, remove receipt
- `FailAsync`: re-enqueue or move to DLQ (`{queue}-dlq` list)
- `GetDeadLetterQueueAsync`: LRANGE on DLQ key
- `RequeueDeadLetterAsync`: scan DLQs, re-enqueue to main queue
- `Visibility timeout`: background timer scans sorted set for expired tasks and re-enqueues them
- Background timer also handles pending re-enqueue sorted sets (scheduled retry delay)

**`src/agent/Config/AppConfig.cs`** — added `Redis` property of type `Hercules.Mesh.Backends.Redis.RedisMeshConfig`.

**`src/agent/Mesh/MeshServiceExtensions.cs`** — added `using Hercules.Mesh.Backends.Redis` and `using StackExchange.Redis`; `RegisterMeshBackends` helper registers Redis backends (bus, queue, state store) when `appConfig.Redis.Enabled` or active profile is `Redis`.

**`src/agent/Hercules.csproj`** — added `StackExchange.Redis 2.8.16` NuGet package.

## Validation

- Build: `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors (pre-existing OpenTelemetry vulnerability warnings unchanged)
- Tests: `dotnet test --filter Redis` — 31/31 passed
- Full suite: 1675/1684 (9 pre-existing failures: OtelService × 5, BudgetGuard × 1, NumericValidator × 2, BusHttpServer × 1 — unchanged)

## Scope / Likely files
src/agent/Mesh/Backends/Redis/, src/agent/Mesh/Backends/Redis/RedisMeshBus.cs, src/agent/Mesh/Backends/Redis/RedisTaskQueue.cs

## Dependencies
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md)
- блокирует / опирается на: [task_070 — backend-profiles-degradation](task_070.md)

## Risks / Rollback
Зависимость от внешнего сервиса; graceful degradation в in-process режим при недоступности.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
