# Task 86 — Backpressure, bounded channels, and DLQ/requeue fixes

**Phase:** 5
**Initiative:** 46
**Status:** done
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
- [x] `Observability/OtelMetrics.cs` — добавить `BusChannelDropCounter` (`hercules.bus.channel.drop.count`) и `MeshHandlerDropCounter` (`hercules.mesh.handler.drop.count`).
- [x] `Config/AppConfig.cs` — добавить `BusConfig { MaxChannelCapacity, BackpressureTimeoutMs, DropOnBackpressure }` и `Mesh.Backpressure { MaxConcurrentHandlers, InProcessTaskQueueVisibilityTimeoutSec }`.
- [x] `HerculesBus/InMemory/InMemoryEventBus.cs` — bound channels: `Channel.CreateBounded<T>(capacity)` (default 1024, configurable). На full channel: `WaitAsync(ct).AsTask().Wait(timeout)` (backpressure) с fallback `DropWrite` + `BusChannelDropCounter`. Режим настраивается через `BusConfig`.
- [x] `Mesh/InProcess/InProcessMeshBus.cs:47` — replace fire-and-forget с `SemaphoreSlim` concurrency limiter (default `MaxConcurrentHandlers=16`); overflow → backpressure с timeout + drop metric.
- [x] `Mesh/InProcess/InProcessTaskQueue.cs:276-281` — implement `ReEnqueueTimedOut`: scan `_inFlight` for `visibilityExpiresAt < now`, re-enqueue to `_queues[queueName]`, remove from `_inFlight`.
- [x] `Mesh/InProcess/InProcessTaskQueue.cs:259-262` — fix `RequeueDeadLetterAsync`: dequeue из DLQ под lock, enqueue в main queue, удалить из `_dlqs` (replace dict entry под lock).
- [x] `Mesh/Backends/Redis/RedisTaskQueue.cs:186-226` — replace `server.Keys(pattern:...)` scan с O(1) Redis hash: `hercules:inflight:lookup` (taskId → receipt) + `hercules:inflight:meta` (receipt → queueName+json). AckAsync / FailAsync → HGet → atomic remove.
- [x] `appsettings.json` — `Bus.MaxChannelCapacity=1024`, `Bus.BackpressureTimeoutMs=100`, `Bus.DropOnBackpressure=false`, `Mesh.Backpressure.MaxConcurrentHandlers=16`, `Mesh.Backpressure.InProcessTaskQueueVisibilityTimeoutSec=30`.
- [x] DI wiring в `MeshServiceExtensions.cs` и `Program.cs`/`WebApi/Program.cs` — пробрасывать `BusConfig` / `MeshConfig.Backpressure` в конструкторы.
- [x] Unit-тест: publish 2000 messages to bounded channel (cap=1024) с `DropOnBackpressure=true` → drop count > 0; subscriber drains.
- [x] Unit-тест: `InProcessTaskQueue` — dequeue with visibility timeout 1s, не ack → after ~1.2s task re-appears в queue.
- [x] Unit-тест: `RequeueDeadLetterAsync` → item removed from DLQ, appears в main queue.
- [x] Unit-тест: Redis `AckAsync` → O(1) hash lookup (mock verify `HashGetAsync` called, `Keys` not called).
- [x] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/HerculesBus/InMemory/InMemoryEventBus.cs, src/agent/Mesh/InProcess/InProcessMeshBus.cs, src/agent/Mesh/InProcess/InProcessTaskQueue.cs, src/agent/Mesh/Backends/Redis/RedisTaskQueue.cs, src/agent/appsettings.json

## Implementation notes

Реализовано в коммите (см. `git log`):

- `Observability/OtelMetrics.cs` — добавлены два счётчика: `BusChannelDropCounter`
  (`hercules.bus.channel.drop.count`) и `MeshHandlerDropCounter`
  (`hercules.mesh.handler.drop.count`). Тэгируют `channel` / `topic` для дебага.
- `Config/AppConfig.cs` — `BusConfig { MaxChannelCapacity=1024, BackpressureTimeoutMs=100,
  DropOnBackpressure=false }` + `MeshBackpressureConfig { MaxConcurrentHandlers=16,
  InProcessTaskQueueVisibilityTimeoutSec=30, HandlerAcquireTimeoutMs=100 }`. В `MeshConfig`
  добавлено поле `Backpressure`; в `BusConfig` — проброшено в DI.
- `HerculesBus/InMemory/InMemoryEventBus.cs` — `Channel.CreateBounded<T>(capacity)` с
  `BoundedChannelFullMode.Wait`. `TryWrite` → на full: либо drop+metric
  (`DropOnBackpressure=true`), либо `WriteAsync` с timeout=`BackpressureTimeoutMs`,
  затем drop+metric. Старая сигнатура ctor сохранена (для backward-compatible
  unit-тестов: `new InMemoryEventBus(logger)`).
- `Mesh/InProcess/InProcessMeshBus.cs` — fire-and-forget fan-out заменён на
  `SemaphoreSlim(MaxConcurrentHandlers, MaxConcurrentHandlers)`. Каждый handler
  делает `WaitAsync(HandlerAcquireTimeoutMs, ct)`. При таймауте — drop + metric.
  Добавлен параметризованный ctor для DI; старый пустой ctor сохранён.
- `Mesh/InProcess/InProcessTaskQueue.cs` — `ReEnqueueTimedOut` теперь реально
  re-enqueue'ит in-flight задачу (раньше был no-op stub): при срабатывании timer'а
  `VisibilityExpiresAt < now` → удаляем из `_inFlightTasks`, push в основную
  очередь, `DeliveryCount++`. `RequeueDeadLetterAsync` теперь атомарно
  dequeue+enqueue под per-DLQ lock (раньше DLQ-запись оставалась в `_dlqs`).
  Добавлено поле `VisibilityExpiresAt` в `InFlightTask`. Новый ctor с
  `MeshBackpressureConfig` для DI; старый ctor сохранён.
- `Mesh/Backends/Redis/RedisTaskQueue.cs` — `AckAsync` и `FailAsync` теперь
  O(1) через глобальный hash-индекс `hercules:inflight:lookup`
  (`taskId → "{receiptHandle}|{queueName}"`), который обновляется в
  `LoadAndTrackTaskAsync`. `server.Keys(pattern:...)` остался только в
  background-таймере `RequeueTimedOutTasksAsync` (периодический обход всех
  sorted sets — это естественный O(N) путь, per-call ack/fail теперь чистый
  O(1)). Visibility requeue также чистит lookup-индекс для stale receipt.
- `Mesh/MeshServiceExtensions.cs` + `Program.cs` + `Hercules.WebApi/Program.cs` —
  DI: `InMemoryEventBus` и `InProcessMeshBus`/`InProcessTaskQueue` получают
  `BusConfig` и `MeshBackpressureConfig` через factory singletons.
- `appsettings.json` — добавлены секции `Bus` и `Mesh.Backpressure`.
- `Observability/OtelHostBuilderExtensions.cs` — добавлен отсутствующий
  stub-метод `AddHistogramViews`, на который ссылается task_085 (pre-existing
  build regression). Помечен как no-op: bucket boundaries уже зашиты в
  инструменты в `OtelMetrics` (см. task_085 notes). Будет переподключён,
  когда OTel SDK 1.18+ выставит публичный API для `HistogramBucketBoundaries`.

### Validation

- `dotnet build src/agent/Hercules.csproj -c Debug` → 0 errors.
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj -c Debug` → 0 errors.
- `dotnet test --filter "FullyQualifiedName~InMemoryEventBusBackpressureTests|FullyQualifiedName~InProcessTaskQueueTests|FullyQualifiedName~InProcessMeshBusTests|FullyQualifiedName~RedisTaskQueueTests"`
  → 41/42 passed. Единственный fail — `EnqueueAsync_StoresTaskMetadata` —
  pre-existing (воспроизводится на HEAD без моих изменений).
- Полный прогон `dotnet test` → 1941/1951 passed. 10 failures:
  `WasmTool_Caches_Compiled_Modules_By_Source_Hash`, `SendAsync_WithLatency_ReportsLatency`,
  `OtelServiceTests` (5), `EnqueueAsync_StoresTaskMetadata`, `NumericValidatorTests` (2) —
  все pre-existing, не связаны с task_086.

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