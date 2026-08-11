# Task 17 — Безопасное самоулучшение

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `safe-self-improvement`

## Goal
Maintenance workflow анализирует анонимизированные failures и eval, предлагает versioned skill diff, тесты, ожидаемый gain и rollback plan. Не активирует свои изменения вне approval policy.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Reflection/MaintenanceWorkflow.cs, src/agent/Reflection/ProposalDiffer.cs

## Dependencies
- блокирует / опирается на: [task_002 — skill-lifecycle](task_002.md)
- блокирует / опирается на: [task_014 — audit-privacy](task_014.md)
- блокирует / опирается на: [task_016 — eval-harness](task_016.md)

## Risks / Rollback
Само-модификация без human gate; строгий two-person rule для prod-skills.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
