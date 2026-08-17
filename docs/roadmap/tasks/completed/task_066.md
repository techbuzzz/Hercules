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
- [x] All abstractions registered in `MeshServiceCollectionExtensions`
- [x] Unit tests for all three interfaces covering happy path + error cases
- [x] `dotnet build` passes; `dotnet test` passes

## Sub-tasks (implementation checklist)
- [x] Define IMeshBus with Publish/Subscribe/RequestReply/IsHealthy
- [x] Define ITaskQueue with Enqueue/Dequeue/Ack/Fail/DLQ + MeshTask/QueuedTask records
- [x] Define IMeshStateStore with Get/Set/CompareAndSet/Delete/Exists/Increment/Watch/ScanKeys/IsHealthy + StoredValue record
- [x] Implement InProcessMeshBus (Channel-based pub/sub, ConcurrentDictionary subscribers, reply routing via TCS)
- [x] Implement InProcessTaskQueue (ConcurrentQueue + Timer for visibility timeout, DLQ, retry with delay, ULID IDs)
- [x] Implement InProcessMeshStateStore (ConcurrentDictionary + Timer for TTL cleanup, Channel for Watch)
- [x] Register all three as singleton defaults in MeshServiceCollectionExtensions
- [x] Write InProcessMeshBusTests (9 tests: BackendKind, IsHealthy, Publish, Subscribe fan-out, unsubscribe, RequestReply, timeout, Dispose)
- [x] Write InProcessTaskQueueTests (14 tests: BackendKind, Enqueue, Dequeue, FIFO, Ack, Fail+retry, Fail→DLQ, DLQ read, IsHealthy, Dispose)
- [x] Write InProcessMeshStateStoreTests (16 tests: BackendKind, Get/Set, CAS, Delete, Exists, Increment, Watch, ScanKeys, IsHealthy, Dispose)
- [x] Fix bug: missing `ScheduleReEnqueueAsync` method in InProcessTaskQueue (broken ContinueWith state object)
- [x] Fix bug: DLQ name mutation in FailAsync (`queueName` → `queueName + "-dlq"` caused `_dlqs["dlq-q-dlq"]` vs `_dlqs["dlq-q-dlq-dlq"]` mismatch)

## Validation
```
dotnet build src/agent/Hercules.csproj  → 0 errors
dotnet test --filter "FullyQualifiedName~InProcess"
  → Passed: 42, Failed: 0
```

## Completion note
Implemented three mesh backend abstractions (IMeshBus, ITaskQueue, IMeshStateStore) with in-process defaults:
- **IMeshBus**: Channel-based pub/sub + request/reply with correlation-ID routing via TaskCompletionSource
- **ITaskQueue**: ConcurrentQueue with visibility timeout via Timer, DLQ, retry with delay, ULID IDs; fixes: missing `ScheduleReEnqueueAsync` helper, DLQ key mutation causing wrong queue lookup
- **IMeshStateStore**: ConcurrentDictionary with version-based optimistic locking, TTL cleanup, Channel-based Watch
- All registered as singleton defaults in MeshServiceCollectionExtensions (lines 440-445)
- 42 unit tests covering happy path and error cases

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
