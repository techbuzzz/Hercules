# Task 51 — Shared memory sync

**Phase:** 4
**Initiative:** 21
**Status:** pending
**Owner:** —
**Slug:** `shared-memory-sync`

## Goal
Избранные факты и навыки синхронизируются только между trusted агентами: explicit namespaces, provenance, conflict resolution, TTL, encryption in transit, per-field data-classification policy.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/SharedMemory/

## Dependencies
- блокирует / опирается на: [task_011 — layered-memory](task_011.md)
- блокирует / опирается на: [task_014 — audit-privacy](task_014.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Утечка чувствительных данных; data classification enforcement.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
