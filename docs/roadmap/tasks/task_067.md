# Task 67 — Redis/Valkey coordination backend

**Phase:** 4
**Initiative:** 27
**Status:** pending
**Owner:** —
**Slug:** `redis-coordination-backend`

## Goal
Опциональный RESP-совместимый in-memory бэкенд (Redis или Valkey) даёт working memory, distributed locks и эфемерные очереди для координации при высокой concurrency. Durable truth остаётся в SQLite/PostgreSQL. Бэкенд активируется профилем развёртывания, не обязан быть запущен.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Backends/Redis/, src/agent/Mesh/Backends/Redis/RedisMeshBus.cs, src/agent/Mesh/Backends/Redis/RedisTaskQueue.cs

## Dependencies
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md)
- блокирует / опирается на: [task_070 — backend-profiles-degradation](task_070.md)

## Risks / Rollback
Зависимость от внешнего сервиса; graceful degradation в in-process режим при недоступности.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
