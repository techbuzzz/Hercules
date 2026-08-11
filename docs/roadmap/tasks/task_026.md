# Task 26 — Least-privilege grants

**Phase:** 2
**Status:** pending
**Owner:** —
**Slug:** `least-privilege-grants`

## Goal
Навык получает только объявленные capabilities. Runtime проверяет grant при вызове; импорт навыка не может тихо расширить разрешения.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Tools/Grants/, src/agent/Skills/Import/

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)
- блокирует / опирается на: [task_020 — skill-manifest](task_020.md)

## Risks / Rollback
Обратная совместимость со старыми навыками; миграционный режим.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
