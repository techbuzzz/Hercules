# Task 37 — Опции транспорта

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `transports`

## Goal
HTTP и gRPC first-class; опциональный адаптер для RabbitMQ, NATS и Azure Service Bus для асинхронных/disconnected сред.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Transport/

## Dependencies
- блокирует / опирается на: [task_035 — delegation-envelope](task_035.md)

## Risks / Rollback
Разные гарантии доставки; абстракция + per-transport caveats.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
