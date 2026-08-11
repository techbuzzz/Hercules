# Task 18 — Устойчивый жизненный цикл задач

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `durable-task-lifecycle`

## Goal
Долгие задачи имеют task ID, state transitions, cancellation, retry, resumable checkpoints, idempotency key. Синхронные chat-запросы остаются простыми.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Tasks/, src/agent/Storage/SqliteSessionStore.cs

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует / опирается на: [task_012 — budget-guardrails](task_012.md)

## Risks / Rollback
Сложность recovery-сценариев; обязательны chaos-тесты.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
