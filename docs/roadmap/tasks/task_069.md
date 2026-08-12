# Task 69 — PostgreSQL shared state backend

**Phase:** 4
**Initiative:** 29
**Status:** pending
**Owner:** —
**Slug:** `postgres-shared-state`

## Goal
Опциональный PostgreSQL state store хранит cross-agent workflow state, shared skill registry, evaluation records и audit logs. Job-очереди используют `SELECT … FOR UPDATE SKIP LOCKED` для умеренно-throughput исполнения. Бэкенд активируется профилем, не обязателен.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Backends/Postgres/, src/agent/Mesh/Backends/Postgres/PostgresStateStore.cs, src/agent/Mesh/Backends/Postgres/PostgresTaskQueue.cs, migrations/postgres/

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md)
- блокирует / опирается на: [task_070 — backend-profiles-degradation](task_070.md)

## Risks / Rollback
Operational overhead БД; миграции, бэкапы, connection pooling; чёткие runbooks.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
