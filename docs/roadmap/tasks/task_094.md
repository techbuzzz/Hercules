# Task 94 — Backup & SLO panel

**Phase:** 7
**Initiative:** 48
**Status:** pending
**Owner:** —
**Slug:** `web-backup-slo-panel`

## Goal
`BackupController` (task_063) и `SloController` (task_064) — критичные операционные эндпойнты: ручной backup, восстановление из архива, листинг архивов, SLO-определения и текущий burn rate. Web не использует ни один из них. Нужна страница, которая даёт оператору возможность одной кнопкой сделать backup и видеть здоровье SLO.

## Acceptance criteria

### Sub-tasks

- [ ] `src/lib/api.ts` — добавить `listBackups(): Promise<BackupEntryDto[]>`, `createBackup(label?): Promise<BackupEntryDto>`, `restoreBackup(id, confirmPassphrase?): Promise<RestoreResultDto>`, `getSlos(): Promise<SloDefinitionDto[]>`, `getSloStatus(id): Promise<SloStatusDto>`
- [ ] Типы: `BackupEntryDto` (id, createdAt, sizeBytes, kind: full|incremental, label, sha256, encrypted), `RestoreResultDto` (id, status, restoredAt, warnings[]), `SloDefinitionDto` (id, name, target, windowDays, errorBudgetMinutes), `SloStatusDto` (current, target, burnRate, errorBudgetRemaining, lastIncidentAt)
- [ ] `src/components/BackupPanel.astro` — таблица архивов (id, createdAt, size, kind, encrypted badge); кнопка «Create backup» (с input для label); кнопка «Restore» per-row с passphrase-input и double-confirm
- [ ] `src/components/SloPanel.astro` — список SLO с progress bar (target vs current), error-budget remaining %, 1h/6h/24h burn-rate, last incident timestamp
- [ ] Новая страница `src/pages/ops.astro` — обе панели на одном layout
- [ ] Add navigation entry в `Layout.astro` (`active?: "ops"`)
- [ ] `src/hercules-web/README.md` — обновить
- [ ] `npm run build` — exit 0
- [ ] Manual smoke: create backup → запись появляется в таблице через 1-2s; restore dry-run (если backend поддерживает) → success response; SLO панель показывает актуальные цифры
