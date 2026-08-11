# Task 34 — Capability registry

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `capability-registry`

## Goal
Локальный файл или SQLite-реестр: известные агенты, capabilities, endpoint health, trust level, поддержка протоколов, cost/latency hints, expiry.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Registry/

## Dependencies
- блокирует / опирается на: [task_032 — agent-manifest](task_032.md)

## Risks / Rollback
Stale entries; health-check + TTL обязательны.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
