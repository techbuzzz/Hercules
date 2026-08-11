# Task 55 — Security operations

**Phase:** 5
**Status:** pending
**Owner:** —
**Slug:** `security-ops`

## Goal
Fleet-wide identity rotation, credential revocation, certificate renewal, проверка подписи пакетов, vulnerability reporting, security audit export.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Security/

## Dependencies
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)
- блокирует / опирается на: [task_021 — skill-marketplace](task_021.md)
- блокирует / опирается на: [task_039 — identity-delegation](task_039.md)

## Risks / Rollback
Сложность key management; интеграция с KMS.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
