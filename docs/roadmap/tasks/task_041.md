# Task 41 — Inter-agent audit trail

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `inter-agent-audit`

## Goal
Каждая делегация записывает sender, receiver, intent, payload hash, data classification, policy decision, cost, latency, response hash, исход; связано по traceId.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Audit/

## Dependencies
- блокирует / опирается на: [task_014 — audit-privacy](task_014.md)
- блокирует / опирается на: [task_035 — delegation-envelope](task_035.md)

## Risks / Rollback
Размер логов; ротация и архив.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
