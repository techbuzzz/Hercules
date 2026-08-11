# Task 36 — Протокол жизненного цикла задач

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `task-lifecycle-protocol`

## Goal
Delegated задачи: accepted, working, awaiting-input, completed, failed, cancelled, expired; вызывающий может poll, subscribe или callback по возможностям transport.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/TaskLifecycle/

## Dependencies
- блокирует / опирается на: [task_018 — durable-task-lifecycle](task_018.md)
- блокирует / опирается на: [task_035 — delegation-envelope](task_035.md)

## Risks / Rollback
Согласованность состояния при сбоях; idempotent transitions.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
