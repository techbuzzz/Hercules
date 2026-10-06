# Task 94 — Backup & SLO panel

**Phase:** 7
**Initiative:** 48
**Status:** done
**Owner:** —
**Slug:** `web-backup-slo-panel`

## Goal
`BackupController` (task_063) и `SloController` (task_064) — критичные операционные эндпойнты: ручной backup, восстановление из архива, листинг архивов, SLO-определения и текущий burn rate. Web не использует ни один из них. Нужна страница, которая даёт оператору возможность одной кнопкой сделать backup и видеть здоровье SLO.

## Acceptance criteria

### Sub-tasks

- [x] `src/lib/api.ts` — добавить Backup + SLO методы: `listBackups`, `createBackup`, `restoreBackup`, `verifyBackup`, `deleteBackup`, `getSloSummary`, `getSloDefinition`, `getSloStatus`, `getSloReport`, `ackSloViolation`, `ackAllSloViolations`
- [x] DTO под реальный wire-формат: `BackupSummaryDto` (id, filename, created_at, size_bytes, encrypted, key_fingerprint, components, files_count), `BackupResultDto` (BackupId, ArchivePath, SizeBytes, UncompressedBytes, FilesCount, Encrypted, KeyFingerprint, Duration, Warnings), `RestoreResultDto` (BackupId, Success, FilesRestored, FilesSkipped, Errors, Warnings, Duration), `VerifyResultDto` (BackupId, Valid, Encrypted, KeyFingerprint, FileHashesValid, FileHashesInvalid, Warnings, Duration), `SloSummaryDto` (Timestamp, Verticals, TotalVerticals, OkCount, WarningCount, CriticalCount), `SloDefinitionDto` (Vertical, Description, Version, AvailabilityTarget, ResponseTimeTargetMs, DataLossTargetPerDay, RecoveryTimeTargetMinutes, CostTargetUsd, AlertThresholds, Runbooks), `SloStatusDto` (Vertical, OverallSeverity, Timestamp, Objectives, RecentViolations, IsAcknowledged, AcknowledgedBy, AcknowledgedAt), `SloObjectiveStatusDto`, `SloViolationRecordDto`, `SloReportDto`, `SloComplianceSummaryDto`
- [x] `src/components/BackupPanel.astro` — таблица архивов (id, createdAt, size, encrypted badge, components pills, filesCount); кнопка «Create backup» (с optional passphrase-input); кнопки «Verify», «Restore» (passphrase-input + confirm), «Delete» per-row
- [x] `src/components/SloPanel.astro` — список verticals (severity pills, objectives progress bar target vs current, violations summary, ack-кнопки); детальный view per vertical (definition targets, runbooks preview)
- [x] Новая страница `src/pages/ops.astro` — обе панели на одном layout
- [x] Add navigation entry в `Layout.astro` (`active?: "ops"`)
- [x] `src/hercules-web/README.md` — обновить
- [x] `npm run build` — exit 0
- [x] `npx astro check` — 0 errors/warnings в новых файлах
- [x] Manual smoke: build artefacts проверены; страница /ops рендерится с активной nav-link; bundle'ы `BackupPanel.*.js` (7.6KB) и `SloPanel.*.js` (11.4KB) присутствуют в `dist/_astro/`

## Implementation notes

### Round 1 (this tick) — completed

Backup & SLO панель для новой страницы `src/pages/ops.astro` (Phase 7 task_094).

**API (src/lib/api.ts) — добавлены 11 методов и 15 DTO:**

Backup:
- `listBackups()` → `GET /api/backups` → `BackupSummaryDto[]`
- `createBackup(passphrase?)` → `POST /api/backups` → `BackupResultDto`
- `restoreBackup(id, passphrase?, targetDir?)` → `POST /api/backups/{id}/restore` → `RestoreResultDto`
- `verifyBackup(id, passphrase?)` → `GET /api/backups/{id}/verify?passphrase=…` → `VerifyResultDto`
- `deleteBackup(id)` → `DELETE /api/backups/{id}` → 204

SLO:
- `getSloSummary()` → `GET /api/slos` → `SloSummaryDto`
- `getSloDefinition(vertical)` → `GET /api/slos/{vertical}/definition` → `SloDefinitionDto`
- `getSloStatus(vertical)` → `GET /api/slos/{vertical}` → `SloStatusDto`
- `getSloReport(vertical)` → `GET /api/slos/{vertical}/report` → `SloReportDto`
- `ackSloViolation(vertical, violationId, by?)` → `POST /api/slos/{vertical}/ack/{violationId}?acknowledgedBy=…`
- `ackAllSloViolations(vertical, by?)` → `POST /api/slos/{vertical}/ack?acknowledgedBy=…`

**Расхождение DTO со spec'ом:**
Спека перечисляла `BackupEntryDto` (id, createdAt, sizeBytes, kind: full|incremental, label, sha256, encrypted) и `SloDefinitionDto` (id, name, target, windowDays, errorBudgetMinutes). Реальный wire-формат использует разные поля. `kind: full|incremental` — backend не имеет incremental-бэкапов (все full). `label` и `sha256` — отсутствуют в `BackupSummary` (filename + size_bytes + components + files_count). `SloDefinition` использует `vertical`/`description`/`availabilityTarget`/`responseTimeTargetMs`/... (per-objective, не per-window errorBudgetMinutes). DTO в `api.ts` зеркалит реальный wire-формат, чтобы избежать полей-призраков; в комментариях отмечено расхождение.

**BackupPanel (src/components/BackupPanel.astro) — новый:**
- Header: title, last-updated, auto-refresh чекбокс (15s, opt-in), refresh button.
- «Create backup» форма: optional passphrase input + submit. После успеха — `showOk` с backup id / size / files / duration.
- Таблица архивов: id (mono, trunc до 14rem filename), createdAt (локально), size (KB/MB/GB), files_count, components pills (config/skills/memory/sqlite/identity, line-through если false), encryption pill (encrypted/plain) + key_fingerprint, action buttons (Verify / Restore / Delete).
- Inline restore-modal: passphrase (опц.), targetDir (опц.), confirm-чекбокс («понимаю, что перезапишет») — submit enabled только при подтверждении. ESC и клик по фону закрывают.
- `verify` → success `showOk` или error `showError` с invalid hashes; `delete` — `window.confirm`, потом `DELETE /api/backups/{id}`.
- TimeSpan `Duration` парсится из ISO 8601 (`/^PT...S$/`) и форматируется как ms/s/m.

**SloPanel (src/components/SloPanel.astro) — новый:**
- Header: title, last-updated, auto-refresh чекбокс (30s, opt-in), refresh.
- Summary grid: 4 карточки (total / ok / warning / critical) с severity-pill.
- Per-vertical card: severity pill, timestamp, ack-by badge; objectives grid (progress bar targetAchievementPct, severity-coloured bar emerald/amber/rose); recent violations (severity + objective + violationId + breachDescription + detected/resolved/ack); `<details>` с definition targets (avail/P95/loss/recovery/cost/version); compliance summary (windowDays + availability/P95/overall/dataLossTotal/maxRecovery/totalCost).
- «Ack all violations» появляется если есть un-acked; per-row «Ack» у каждой violation.
- Definition + report подгружаются параллельно с `Promise.all` per vertical; частичные сбои swallow (status — must-have).
- Auto-refresh каждые 30s, opt-in (как у соседних panels).

**Страница (src/pages/ops.astro):**
- `<BackupPanel />` сверху, `<SloPanel />` снизу, gap-8.
- Использует `<Layout active="ops">`.

**Layout (src/layouts/Layout.astro):**
- Добавлен `"ops"` в union `active`; nav-link `Ops → /ops` (после Observability, перед A2A).

**README (src/hercules-web/README.md):**
- В «Структура» добавлены `BackupPanel.astro`, `SloPanel.astro`, `ops.astro`.
- В «Backend API» добавлены 10 эндпоинтов (5 backup + 5 slo + ack-all в одной строке).

### Round 1 — validation

- `npm run build` → **exit 0**, 9 страниц собрано (`/`, `/a2a`, `/config`, `/memmesh`, `/observability`, `/ops`, `/profile`, `/skills`, `/stats`). Bundle'ы: `BackupPanel.*.js` ≈ 7.6 KB, `SloPanel.*.js` ≈ 11.4 KB.
- `npx astro check` → 0 errors, 0 warnings, 0 hints в новых файлах (`BackupPanel.astro`, `SloPanel.astro`, `ops.astro`, `lib/api.ts`, `layouts/Layout.astro`). Pre-existing 17 errors в `MeshRouterPanel.astro` (вне scope task_094; задокументировано в task_088/089).
- Проверка `dist/ops/index.html`: `id="backup-panel"` + `id="slo-panel"` присутствуют; nav-link `/ops` активен (`bg-white/10 text-white`); title `Hercules · Operations` корректный.
