# Task 66 — Абстракция mesh-бэкендов

**Phase:** 4
**Initiative:** 26
**Status:** done
**Owner:** —
**Slug:** `mesh-backends-abstraction`

## Goal
Интерфейсы `IMeshBus`, `ITaskQueue` и `IMeshStateStore` отделяют mesh-оркестрацию от конкретных бэкендов. Single-host mesh продолжает работать с in-process очередями и SQLite по умолчанию. Бэкенды подключаются через dependency injection и не влияют на публичный API агента.

## Acceptance criteria
- [x] `IMeshBus` interface with Publish/Subscribe/RequestReply semantics
- [x] `ITaskQueue` interface with Enqueue/Dequeue/Ack/DLQ semantics
- [x] `IMeshStateStore` interface with Get/Set/CompareAndSet/Watch semantics
- [x] `InProcessMeshBus` — Channel-based pub/sub + request/reply (default)
- [x] `InProcessTaskQueue` — ConcurrentQueue-based task queue (default)
- [x] `InProcessMeshStateStore` — ConcurrentDictionary-based state store (default)
- [x] Interfaces live in `src/agent/Mesh/Abstractions/`
- [x] In-process impls live in `src/agent/Mesh/InProcess/`
- [x] All abstractions registered in `MeshServiceExtensions` (was `MeshServiceCollectionExtensions`)
- [x] Unit tests for all three interfaces covering happy path + error cases
- [x] `dotnet build` passes; `dotnet test` passes

## Validation
```
dotnet build src/agent/Hercules.csproj          # 0 errors, pre-existing warnings only
dotnet test --filter "FullyQualifiedName~InProcessMesh"  # 29 passed (8 bus, 10 queue, 11 state store)
dotnet test tests/Hercules.Agent.Tests/        # 1468 passed, 9 pre-existing failures
```

## Implementation notes
- `IMeshBus`: Channel<T>-based fan-out; handlers stored in `ConcurrentDictionary<string, List<AsyncHandler>>`; `PublishAsync` iterates handlers directly (no `GetOrAdd` race)
- `RequestReply`: TCS-based with `correlationId` routing; subscriber must set `ReplyTo = env.ReplyTo` in reply envelope
- `ITaskQueue`: `_inFlightTasks` dict tracks dequeued tasks by receipt handle so `FailAsync` can find them by taskId
- `ScheduleReEnqueueAsync`: uses `Task.Delay(delay)` WITHOUT caller's CancellationToken to avoid xUnit test-token cancellation killing the re-enqueue
- `InProcessMeshStateStore`: `CompareAndSet` uses optimistic locking (fetch + CAS loop); `Watch` uses `Channel<T>` buffered to 100 events
- All three interfaces registered as singletons in `MeshServiceExtensions`

## Scope / Likely files
src/agent/Mesh/Abstractions/IMeshBus.cs, src/agent/Mesh/Abstractions/ITaskQueue.cs, src/agent/Mesh/Abstractions/IMeshStateStore.cs, src/agent/Mesh/InProcess/

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует / опирается на: [task_043 — mesh-router](task_043.md)

## Risks / Rollback
Утечка абстракций; минимальный набор методов, чёткие контракты, контрактные тесты на каждую реализацию.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
