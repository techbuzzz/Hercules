# Task 7 — Типизированные контракты агента

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `typed-contracts`

## Goal
Запросы, планы, вызовы и результаты инструментов, записи памяти и ответы используют версионированные JSON-схемы и C# типы; некорректный LLM-вывод чинится один раз или отклоняется.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Contracts/, src/agent/LLM/JsonRepair/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)

## Risks / Rollback
Слишком жёсткие схемы ограничивают LLM; баланс между strict и permissive.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
