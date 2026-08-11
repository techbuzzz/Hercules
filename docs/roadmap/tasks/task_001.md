# Task 1 — Базовый цикл агента

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `core-agent-loop`

## Goal
AgentCore обрабатывает запрос: определяет применимый навык, опционально планирует ограниченные шаги инструментов, обновляет память, логирует исход.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/AgentCore.cs, src/agent/Loop/

## Dependencies
- нет (стартовая)

## Risks / Rollback
Неопределённый цикл при плохих skill matches; нужен явный max-steps + cancellation.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
