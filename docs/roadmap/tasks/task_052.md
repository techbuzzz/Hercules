# Task 52 — Mesh evaluation suite

**Phase:** 4
**Initiative:** 31
**Status:** pending
**Owner:** —
**Slug:** `mesh-eval-suite`

## Goal
Воспроизводимые сценарии измеряют task success, safety denials, routing quality, latency, cost, resilience и деградацию при отказе peer/tool/LLM-провайдера.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
tests/Hercules.Agent.Tests/Mesh/EvalSuite/, templates/mesh-eval/

## Dependencies
- блокирует / опирается на: [task_042 — protocol-tests](task_042.md)
- блокирует / опирается на: [task_045 — fan-out-in](task_045.md)
- блокирует / опирается на: [task_047 — retry-timeout-breaker](task_047.md)

## Risks / Rollback
Реалистичные сценарии дороги; shared scenario library.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
