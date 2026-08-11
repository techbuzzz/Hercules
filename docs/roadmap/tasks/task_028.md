# Task 28 — Кэширование

**Phase:** 2
**Status:** pending
**Owner:** —
**Slug:** `caching`

## Goal
Deterministic tool results, embeddings, routing decisions и provider-supported prompt prefixes кэшируются с scope, TTL, invalidation и sensitivity-правилами.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Cache/

## Dependencies
- блокирует / опирается на: [task_022 — semantic-routing](task_022.md)

## Risks / Rollback
Stale cache для sensitive данных; per-data-class TTL.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
