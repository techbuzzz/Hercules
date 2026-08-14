# Task 66 — Абстракция mesh-бэкендов

**Phase:** 4
**Initiative:** 26
**Status:** in_progress
**Owner:** —
**Slug:** `mesh-backends-abstraction`

## Goal
Интерфейсы `IMeshBus`, `ITaskQueue` и `IMeshStateStore` отделяют mesh-оркестрацию от конкретных бэкендов. Single-host mesh продолжает работать с in-process очередями и SQLite по умолчанию. Бэкенды подключаются через dependency injection и не влияют на публичный API агента.

## Acceptance criteria
- [ ] `IMeshBus` interface with Publish/Subscribe/RequestReply semantics
- [ ] `ITaskQueue` interface with Enqueue/Dequeue/Ack/DLQ semantics
- [ ] `IMeshStateStore` interface with Get/Set/CompareAndSet/Watch semantics
- [ ] `InProcessMeshBus` — Channel-based pub/sub + request/reply (default)
- [ ] `InProcessTaskQueue` — ConcurrentQueue-based task queue (default)
- [ ] `InProcessMeshStateStore` — ConcurrentDictionary-based state store (default)
- [ ] Interfaces live in `src/agent/Mesh/Abstractions/`
- [ ] In-process impls live in `src/agent/Mesh/InProcess/`
- [ ] All abstractions registered in `MeshServiceCollectionExtensions`
- [ ] Unit tests for all three interfaces covering happy path + error cases
- [ ] `dotnet build` passes; `dotnet test` passes

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
