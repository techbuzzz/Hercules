# Task 42 — Контрактные и chaos тесты

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `protocol-tests`

## Goal
Protocol fixtures проверяют обратную совместимость и обработку malformed messages; локальные test-агенты симулируют таймауты, дубли, недоступность и schema mismatch.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
tests/Hercules.Agent.Tests/Mesh/

## Dependencies
- блокирует / опирается на: [task_035 — delegation-envelope](task_035.md)
- блокирует / опирается на: [task_036 — task-lifecycle-protocol](task_036.md)
- блокирует / опирается на: [task_037 — transports](task_037.md)

## Risks / Rollback
Сложно воспроизводимо; нужен deterministic test harness.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
