# Task 12 — Бюджеты и guardrails

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `budget-guardrails`

## Goal
Per-request и per-day лимиты на токены, стоимость, время, вызовы инструментов, диск и ретраи. Агент сообщает graceful degradation вместо тихого превышения.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Budget/, src/agent/Hercules.WebApi/Controllers/BudgetController.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)
- блокирует / опирается на: [task_008 — bounded-execution](task_008.md)

## Risks / Rollback
Жёсткие лимиты ломают сложные задачи; soft-warn + hard-cap.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
