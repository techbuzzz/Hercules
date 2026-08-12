# Task 70 — Backend-профили и деградация

**Phase:** 4
**Initiative:** 30
**Status:** pending
**Owner:** —
**Slug:** `backend-profiles-degradation`

## Goal
Профили развёртывания объявляют, какой mesh используется: только локальный SQLite, Redis/Valkey, NATS, PostgreSQL или комбинации. Если бэкенд становится недоступен, агенты деградируют в local-only режим или прекращают приём новых делегаций по policy, а не падают молча. Состояние деградации наблюдаемо.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Profiles/, src/agent/Mesh/Profiles/MeshProfileLoader.cs, src/agent/Mesh/Degradation/HealthMonitor.cs

## Dependencies
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md)
- блокирует / опирается на: [task_065 — mesh-observability](task_065.md)
- блокирует / опирается на: [task_067 — redis-coordination-backend](task_067.md)
- блокирует / опирается на: [task_068 — nats-jetstream-transport](task_068.md)
- блокирует / опирается на: [task_069 — postgres-shared-state](task_069.md)

## Risks / Rollback
Тихое падение в degraded-режим без видимости; явный mode flag + alerting + policy-driven refuse-when-stale.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
