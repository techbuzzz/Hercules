# Task 48 — Границы делегации

**Phase:** 4
**Status:** pending
**Owner:** —
**Slug:** `delegation-boundaries`

## Goal
Mesh ограничивает hop count, fan-out width, суммарные tool calls, кумулятивную стоимость и время. Каждый агент может отклонить делегацию, превышающую его policy/capacity.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Budget/

## Dependencies
- блокирует / опирается на: [task_012 — budget-guardrails](task_012.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Ложные отказы; явный reason code в ответе.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
