# Task 44 — Сложность и стоимость

**Phase:** 4
**Initiative:** 17
**Status:** pending
**Owner:** —
**Slug:** `complexity-router`

## Goal
Дешёвое детерминированное правило или маленький классификатор выбирает: direct skill / small model / large model / one peer / fan-out. Решения измеримы и конфигурируемы.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Router/ComplexityRouter.cs

## Dependencies
- блокирует / опирается на: [task_043 — mesh-router](task_043.md)

## Risks / Rollback
Сложность ML-классификатора; версия на rules + опциональный ML.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
