# Task 61 — Local-first degradation

**Phase:** 5
**Status:** pending
**Owner:** —
**Slug:** `local-degradation`

## Goal
Когда cloud LLM, peers или сеть недоступны, агент следует configured safe fallback: deterministic rules, local skills, reduced-capability models, queued work, operator notification.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Degradation/

## Dependencies
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)
- блокирует / опирается на: [task_022 — semantic-routing](task_022.md)
- блокирует / опирается на: [task_060 — offline-resilience](task_060.md)

## Risks / Rollback
Неожиданная silent degradation; явный mode flag + observability.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
