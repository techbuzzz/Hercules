# Task 15 — Секреты и конфигурация

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `secrets-config`

## Goal
appsettings + environment variables; секреты никогда не пишутся в skill packages, Markdown memory, telemetry или export archives.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Config/, src/agent/HostBuilderExtensions.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)

## Risks / Rollback
Случайная утечка в логи; централизованный redaction-фильтр.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
