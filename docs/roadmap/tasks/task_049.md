# Task 49 — Human-in-the-loop эскалация

**Phase:** 4
**Initiative:** 24
**Status:** pending
**Owner:** —
**Slug:** `human-escalation`

## Goal
Mesh эскалирует неоднозначные, low-confidence, policy-sensitive, разрушительные или budget-exceeding операции с кратким action plan и контекстом для подтверждения человеком. Гейты выполнения обеспечиваются кодом, а не только промптами.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Escalation/, src/hercules-web/src/components/EscalationPanel.astro

## Dependencies
- блокирует / опирается на: [task_010 — approval-gates](task_010.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Operator fatigue; агрегация эскалаций + приоритезация + удобный batch-approve.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
