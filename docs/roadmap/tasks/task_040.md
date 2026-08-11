# Task 40 — Trust и admission policy

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `trust-admission`

## Goal
Peer-агенты allow-listed по identity и capability. Вызовы отклоняются, когда intent, data classification, schema version, budget или risk level не разрешены.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Policy/

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)
- блокирует / опирается на: [task_039 — identity-delegation](task_039.md)

## Risks / Rollback
Слишком строго => false negatives; нужны dry-run отчёты.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
