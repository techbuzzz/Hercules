# Task 58 — Configuration и policy rollout

**Phase:** 5
**Initiative:** 41
**Status:** pending
**Owner:** —
**Slug:** `config-policy-rollout`

## Goal
Подписанные версионированные configuration и policy бандлы: staged rollout, local validation, expiry, rollback, last-known-good fallback.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Config/Rollout/

## Dependencies
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)
- блокирует / опирается на: [task_055 — security-ops](task_055.md)

## Risks / Rollback
Bad config rollout; staged + dry-run + monitoring.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
