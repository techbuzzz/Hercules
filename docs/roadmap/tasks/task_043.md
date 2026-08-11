# Task 43 — Mesh router

**Phase:** 4
**Status:** pending
**Owner:** —
**Slug:** `mesh-router`

## Goal
Когда локальный навык недоступен, неприменим или ниже confidence threshold, router выбирает лучший trusted peer по capability, policy, health, latency, quality score и budget.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Router/

## Dependencies
- блокирует / опирается на: [task_022 — semantic-routing](task_022.md)
- блокирует / опирается на: [task_034 — capability-registry](task_034.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Скрытые предпочтения провайдера; явный scoring + observability.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
