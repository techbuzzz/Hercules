# Task 60 — Offline resilience

**Phase:** 5
**Status:** pending
**Owner:** —
**Slug:** `offline-resilience`

## Goal
Edge-агент буферизует sensor logs, task results и outgoing alerts через bounded local queues; возобновляет sync с deduplication и ordering при восстановлении связи.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Offline/

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует / опирается на: [task_018 — durable-task-lifecycle](task_018.md)

## Risks / Rollback
Buffer overflow; явные приоритеты и TTL.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
