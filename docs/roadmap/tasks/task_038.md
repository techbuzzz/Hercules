# Task 38 — Механизмы discovery

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `discovery`

## Goal
Static config, registry lookup и mDNS/Bonjour для deployment-specific discovery. Discovery сам по себе не выдаёт trust.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Discovery/

## Dependencies
- блокирует / опирается на: [task_034 — capability-registry](task_034.md)

## Risks / Rollback
Spoofing discovery; trust policy отдельно.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
