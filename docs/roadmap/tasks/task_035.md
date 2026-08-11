# Task 35 — Формат inter-agent сообщений

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `delegation-envelope`

## Goal
Версионированный JSON delegation envelope: requestId, traceId, idempotencyKey, sender, recipient, intent, typed payload, replyTo, deadline, auth context, requested response schema.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Envelope/, src/agent/Mesh/Schema/

## Dependencies
- блокирует / опирается на: [task_007 — typed-contracts](task_007.md)

## Risks / Rollback
Эволюция схемы; явные semver + миграции.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
