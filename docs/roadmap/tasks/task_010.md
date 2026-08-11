# Task 10 — Гейты подтверждения

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `approval-gates`

## Goal
Read-only операции идут автоматически; write/delete/network/shell/financial/hardware требуют policy-based подтверждения с показом proposed action оператору.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Tools/Approval/, src/agent/Hercules.WebApi/Controllers/ApprovalController.cs

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)

## Risks / Rollback
UX-фрикция в CLI/Web при частых подтверждениях; батчинг и доверенные scopes.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
