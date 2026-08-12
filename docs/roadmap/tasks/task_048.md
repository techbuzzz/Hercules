# Task 48 — Границы делегации

**Phase:** 4
**Initiative:** 23
**Status:** pending
**Owner:** —
**Slug:** `delegation-boundaries`

## Goal
Mesh ограничивает hop count, fan-out width, кумулятивные tool calls, общую стоимость и время на запрос. Агенты могут отклонить делегацию, чтобы не превышать свою policy или capacity.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Budget/

## Dependencies
- блокирует / опирается на: [task_012 — budget-guardrails](task_012.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Слишком жёсткие границы блокируют легитимные задачи; явный reason code + метрики.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
