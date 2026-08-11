# Task 49 — Human-in-the-loop эскалация

**Phase:** 4
**Status:** pending
**Owner:** —
**Slug:** `human-escalation`

## Goal
Mesh эскалирует оператору неоднозначные, low-confidence, policy-sensitive, деструктивные и budget-exceeding операции с concise action plan и approval context.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Escalation/, src/hercules-web/src/components/EscalationPanel.astro

## Dependencies
- блокирует / опирается на: [task_010 — approval-gates](task_010.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Operator fatigue; приоритизация и группировка эскалаций.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
