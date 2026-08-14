# Task 68 — NATS / JetStream transport option

**Phase:** 4
**Initiative:** 28
**Status:** done
**Owner:** —
**Slug:** `nats-jetstream-transport`

## Goal
Опциональный NATS-бэкбон для сообщений: subject-based routing, queue groups для load-balanced agent workers, JetStream-стримы для durable at-least-once доставки и replay при дисконнектах. Активируется профилем, не обязателен для одиночного локального агента.

## Acceptance criteria

### Sub-tasks

- [x] Add `NATS.Client` NuGet packages to `src/agent/Hercules.csproj` (nats.client v2.8.2 + System.Linq.Async)
- [x] `Mesh/Backends/Nats/NatsMeshConfig.cs` — config: Enabled, Servers, Name, AuthToken, UserCredentials, KeyPrefix, StreamPrefix, DefaultVisibilityTimeoutSec, JetStreamEnabled, JetStreamMaxBytes, JetStreamMaxAgeDays
- [x] `Config/AppConfig.cs` — add `Nats Nats { get; set; } = new()` property
- [x] `Mesh/Backends/Nats/NatsMeshBus.cs` — IMeshBus: Publish (NATS core pub/sub), Subscribe (async subscription), RequestReply (request/reply with reply-to subject), IsHealthyAsync
- [x] `Mesh/Backends/Nats/NatsTaskQueue.cs` — ITaskQueue: JetStream streams + consumers; EnqueueAsync, DequeueAsync (pull consumer), AckAsync, FailAsync (retry or DLQ), GetDeadLetterQueueAsync, RequeueDeadLetterAsync, IsHealthyAsync
- [x] `Mesh/Backends/Nats/NatsMeshStateStore.cs` — IMeshStateStore: KV store (JetStream), GetAsync/SetAsync/CompareAndSetAsync/DeleteAsync/ExistsAsync/IncrementAsync/WatchAsync/ScanKeysAsync, IsHealthyAsync
- [x] `Mesh/MeshServiceExtensions.cs` — NATS profile detection (MeshBackendProfile.Nats) + DI registration of all three NATS backends
- [x] Unit tests (`tests/.../Mesh/Backends/Nats/`) — 21 NATS tests pass (BackendKind, IsHealthy, config defaults, idempotent dispose; runtime integration requires NATS server)
- [x] `dotnet build` + `dotnet test` pass (1697 passed, 8 pre-existing failures unrelated to NATS)

## Scope / Likely files
src/agent/Mesh/Backends/Nats/, src/agent/Mesh/Backends/Nats/NatsMeshBus.cs, src/agent/Mesh/Backends/Nats/JetStreamTaskQueue.cs

## Dependencies
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md)
- блокирует / опирается на: [task_037 — transports](task_037.md)
- блокирует / опирается на: [task_070 — backend-profiles-degradation](task_070.md)

## Risks / Rollback
Сложность JetStream; начинать с core NATS + request/reply, JetStream — отдельный milestone. Fallback на in-process bus.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
