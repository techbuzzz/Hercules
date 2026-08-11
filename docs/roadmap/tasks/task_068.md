# Task 68 — NATS / JetStream transport option

**Phase:** 4
**Status:** pending
**Owner:** —
**Slug:** `nats-jetstream-transport`

## Goal
Опциональный NATS-бэкбон для сообщений: subject-based routing, queue groups для load-balanced agent workers, JetStream-стримы для durable at-least-once доставки и replay при дисконнектах. Активируется профилем, не обязателен для одиночного локального агента.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

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
