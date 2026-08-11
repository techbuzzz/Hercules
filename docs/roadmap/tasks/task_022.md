# Task 22 — Семантическая маршрутизация

**Phase:** 2
**Status:** pending
**Owner:** —
**Slug:** `semantic-routing`

## Goal
SkillRouter ранжирует применимые навыки по embedding similarity, lexical match, input-schema compatibility, историческому качеству, latency и policy eligibility.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Skills/Routing/SkillRouter.cs, src/agent/Skills/Routing/Scoring/

## Dependencies
- блокирует / опирается на: [task_002 — skill-lifecycle](task_002.md)
- блокирует / опирается на: [task_016 — eval-harness](task_016.md)

## Risks / Rollback
Зависимость от embedding-провайдера; нужен deterministic fallback (см. задачу 23).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
