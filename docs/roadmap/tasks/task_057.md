# Task 57 — Управление жизненным циклом

**Phase:** 5
**Status:** pending
**Owner:** —
**Slug:** `lifecycle-management`

## Goal
CLI и API: inventory, start, stop, drain, update, canary, health check, rollback, decommissioning агентов и skill packages.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/CLI/Commands/AgentLifecycleCommand.cs, src/agent/Hercules.WebApi/Controllers/LifecycleController.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)

## Risks / Rollback
Случайный downtime; canary + auto-rollback при health-degradation.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
