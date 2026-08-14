# Task 86 — Backpressure, bounded channels, and DLQ/requeue fixes

**Phase:** 5
**Initiative:** 46
**Status:** pending
**Owner:** —
**Slug:** `backpressure-bounded-channels-dlq`

## Goal
Нет backpressure нигде: все channels unbounded, handler dispatch fire-and-forget. DLQ/requeue имеют bugs в in-process queue.

Проблемы:
1. **No backpressure:** `InMemoryEventBus.cs:36,49` — `TryWrite` to unbounded channels. `InProcessMeshBus.cs:47` — fire-and-forget handler invocation, no limit on concurrent handlers.
2. **InProcessTaskQueue visibility timeout stub (Issue 34):** `InProcessTaskQueue.cs:276-281` — `ReEnqueueTimedOut` timer — no-op; timed-out tasks never re-enqueued.
3. **InProcessTaskQueue DLQ requeue broken (Issue 35):** `InProcessTaskQueue.cs:259-262` — `RequeueDeadLetterAsync` builds new DLQ but never replaces old one in `_dlqs` — DLQ item not removed.
4. **RedisTaskQueue O(N) scan (Issue 37):** `RedisTaskQueue.cs:186-226` — `AckAsync`/`FailAsync` scan all in-flight keys via `server.Keys(pattern:...)` per ack — O(N) per operation.

## Acceptance criteria
### Sub-tasks
- [ ] `HerculesBus/InMemory/InMemoryEventBus.cs` — bound channels: `Channel.CreateBounded<T>(capacity)` (default 1024, configurable). `TryWrite` → if full: `WaitAsync` (backpressure) с timeout ИЛИ `DropWrite` policy с metric counter. Configurable per-channel.
- [ ] `Mesh/InProcess/InProcessMeshBus.cs:47` — replace fire-and-forget with `SemaphoreSlim` concurrency limiter for handler invocation (default `MaxConcurrentHandlers=16`). Queue overflow → backpressure + log.
- [ ] `Mesh/InProcess/InProcessTaskQueue.cs:276-281` — implement `ReEnqueueTimedOut`: scan `_inFlight` for `visibilityExpiresAt < now`, re-enqueue to `_queues[queueName]`, remove from `_inFlight`.
- [ ] `Mesh/InProcess/InProcessTaskQueue.cs:259-262` — fix `RequeueDeadLetterAsync`: atomically dequeue from DLQ (`ConcurrentQueue.TryDequeue`) + enqueue to main queue. Use lock or CAS.
- [ ] `Mesh/Backends/Redis/RedisTaskQueue.cs:186-226` — replace `server.Keys(pattern:...)` scan with direct hash lookup: store in-flight mapping `receiptHandle → {queueName, taskId}` in Redis hash `hercules:inflight:{queueName}`. `AckAsync` → `HGet` by receiptHandle (O(1)), then `HDel` + `LRem`.
- [ ] `appsettings.json` — `Bus.MaxChannelCapacity` (default 1024), `Mesh.MaxConcurrentHandlers` (default 16), `Mesh.InProcessTaskQueue.VisibilityTimeoutSec` (default 30).
- [ ] Unit-тест: publish 2000 messages to bounded channel (cap=1024) → publisher blocks или drops с metric; subscriber drains.
- [ ] Unit-тест: `InProcessTaskQueue` — dequeue with visibility timeout 1s, don't ack → after 1.5s task re-appears in queue.
- [ ] Unit-тест: `RequeueDeadLetterAsync` → item removed from DLQ, appears in main queue.
- [ ] Unit-тест: Redis `AckAsync` → O(1) hash lookup (mock verify `HGet` called, `Keys` not called).
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/HerculesBus/InMemory/InMemoryEventBus.cs, src/agent/Mesh/InProcess/InProcessMeshBus.cs, src/agent/Mesh/InProcess/InProcessTaskQueue.cs, src/agent/Mesh/Backends/Redis/RedisTaskQueue.cs, src/agent/appsettings.json

## Dependencies
- блокирует / опирается на: [task_035 — delegation-envelope](task_035.md)
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md)
- блокирует / опирается на: [task_067 — redis-coordination-backend](task_067.md)

## Risks / Rollback
Bounded channels with `WaitAsync` могут deadlock если subscriber slower than publisher; configurable timeout + drop policy. In-process visibility timeout — background timer overhead. Rollback: вернуть unbounded channels (но OOM риск под нагрузкой).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)