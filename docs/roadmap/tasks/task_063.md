# Task 63 — Backup и recovery

**Phase:** 5
**Status:** pending
**Owner:** —
**Slug:** `backup-recovery`

## Goal
Encrypted backups: configuration, skills, selected memory, SQLite data, device identity recovery. Restore-процедуры автоматизированы и протестированы.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Backup/

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует / опирается на: [task_055 — security-ops](task_055.md)

## Risks / Rollback
Encryption key loss; recovery-of-keys обязателен.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
