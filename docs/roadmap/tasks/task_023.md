# Task 23 — Детерминированный fallback маршрутизатора

**Phase:** 2
**Status:** pending
**Owner:** —
**Slug:** `deterministic-router`

## Goal
No-embedding режим: tags, keyword triggers, declared input types; edge deployments остаются работоспособными offline и на ограниченных ресурсах.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Skills/Routing/KeywordRouter.cs

## Dependencies
- блокирует / опирается на: [task_022 — semantic-routing](task_022.md)

## Risks / Rollback
Снижение качества маршрутизации; пользовательский override обязателен.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
