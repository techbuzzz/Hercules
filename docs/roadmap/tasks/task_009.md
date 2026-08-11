# Task 9 — Граница инструментов и policy engine

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `tool-boundary-policy`

## Goal
Локальные инструменты объявляют input/output схемы, side-effect level, required permission, timeout, retry, idempotency. ToolPolicy применяет allow/deny перед вызовом.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Tools/, src/agent/Tools/Policy/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_007 — typed-contracts](task_007.md)

## Risks / Rollback
Сложные правила сложно отлаживать; нужен policy dry-run режим.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
