# Task 39 — Идентичность и делегация

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `identity-delegation`

## Goal
mTLS, API-ключи или OAuth-совместимые bearer-токены для peer-вызовов. Delegated request несёт минимум identity claims и tool authority.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Auth/

## Dependencies
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)

## Risks / Rollback
Token leakage; короткоживущие токены + scope reduction.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
