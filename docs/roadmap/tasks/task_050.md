# Task 50 — Distributed reflection

**Phase:** 4
**Initiative:** 20
**Status:** pending
**Owner:** —
**Slug:** `distributed-reflection`

## Goal
Отчёты рефлексии включают производительность peer-агентов, routing-решения, паттерны сбоев и предлагают улучшения: новые skills, routing-правила, peer-связи. Формируют proposals, а не unreviewed prod-изменения.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Reflection/

## Dependencies
- блокирует / опирается на: [task_017 — safe-self-improvement](task_017.md)
- блокирует / опирается на: [task_041 — inter-agent-audit](task_041.md)

## Risks / Rollback
Перегрузка шумными proposals; rate-limit + ранжирование + человек-в-контуре.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
