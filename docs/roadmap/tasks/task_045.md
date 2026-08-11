# Task 45 — Fan-out / fan-in

**Phase:** 4
**Status:** pending
**Owner:** —
**Slug:** `fan-out-in`

## Goal
Запрос уходит нескольким применимым агентам под строгим concurrency и budget. Ответы schema-validated, выбираются детерминированно, голосованием или опциональным judge.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Router/FanOut.cs, src/agent/Mesh/Aggregation/

## Dependencies
- блокирует / опирается на: [task_043 — mesh-router](task_043.md)
- блокирует / опирается на: [task_044 — complexity-router](task_044.md)

## Risks / Rollback
Стоимость fan-out; строгие per-request лимиты.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
