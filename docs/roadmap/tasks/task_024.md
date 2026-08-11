# Task 24 — Реестр инструментов

**Phase:** 2
**Status:** pending
**Owner:** —
**Slug:** `tool-registry`

## Goal
Навыки объявляют HTTP, FS, shell, DB, GPIO/MQTT и MCP-инструменты из data/Tools/; реестр хранит allow/deny, схемы, лимиты и health state.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Tools/Registry/, src/agent/Tools/Source/

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)

## Risks / Rollback
Безопасность динамической загрузки; подписанные sources.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
