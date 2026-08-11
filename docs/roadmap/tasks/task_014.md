# Task 14 — Аудит и приватность

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `audit-privacy`

## Goal
Каждый side-effect и policy decision имеет audit record (actor, requestId, tool, permission, payload hash, result, timestamp). Конфигурируемая redaction для секретов и PII.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Audit/, src/agent/Redaction/

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)
- блокирует / опирается на: [task_011 — layered-memory](task_011.md)

## Risks / Rollback
Производительность записи в SQLite под нагрузкой; батчинг.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
