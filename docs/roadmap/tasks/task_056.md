# Task 56 — Rate limits и квоты

**Phase:** 5
**Initiative:** 35
**Status:** pending
**Owner:** —
**Slug:** `rate-limits-quotas`

## Goal
Per-agent, per-skill, per-user, per-tenant лимиты concurrency, calls, токенов, оценочной стоимости, storage, message volume.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Quotas/

## Dependencies
- блокирует / опирается на: [task_012 — budget-guardrails](task_012.md)
- блокирует / опирается на: [task_048 — delegation-boundaries](task_048.md)

## Risks / Rollback
Многоуровневые лимиты сложно отлаживать; явный report при отказе.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
