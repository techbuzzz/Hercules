# Task 27 — Сборка и сжатие контекста

**Phase:** 2
**Status:** pending
**Owner:** —
**Slug:** `context-assembly`

## Goal
ContextBuilder выбирает релевантную память, схемы инструментов, примеры и prior task state в рамках token-бюджета; завершённые tool traces сжимаются в episodic memory.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Context/ContextBuilder.cs, src/agent/Context/Summarizer/

## Dependencies
- блокирует / опирается на: [task_011 — layered-memory](task_011.md)
- блокирует / опирается на: [task_012 — budget-guardrails](task_012.md)

## Risks / Rollback
Потеря важного контекста при сжатии; importance-score.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
