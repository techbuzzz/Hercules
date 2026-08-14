# Task 95 — Security, quotas & rollout panel

**Phase:** 7
**Initiative:** 48
**Status:** pending
**Owner:** —
**Slug:** `web-security-quotas-rollout`

## Goal
Три близкие по смыслу операционные подсистемы не имеют web-UI:
- `SecurityOpsController` (task_055) — security events, alerts, suspicious patterns;
- `QuotasController` (task_056) — per-user/per-tool/per-agent quota consumption;
- `RolloutController` (task_058) — config/policy rollout history и staging.

Нужна единая страница «Operations» (расширение task_094 или отдельная) с тремя секциями: Security, Quotas, Rollouts.

## Acceptance criteria

### Sub-tasks

- [ ] `src/lib/api.ts` — добавить `getSecurityEvents(limit?): Promise<SecurityEventDto[]>`, `getSecurityAlerts(): Promise<SecurityAlertDto[]>`, `getQuotas(): Promise<QuotaDto[]>`, `getQuotaForSubject(subject): Promise<QuotaDto>`, `listRollouts(): Promise<RolloutDto[]>`, `getRollout(id): Promise<RolloutDto>`
- [ ] Типы: `SecurityEventDto` (id, timestamp, actor, action, severity, source, details), `SecurityAlertDto` (id, openedAt, severity, summary, relatedEvents[]), `QuotaDto` (subject, kind, current, limit, period, windowStart, windowEnd, isOver), `RolloutDto` (id, kind, startedAt, completedAt, status, summary, affectedSubjects[])
- [ ] `src/components/SecurityOpsPanel.astro` — список alerts (severity pills), expandable event-list per alert, фильтр по severity
- [ ] `src/components/QuotasPanel.astro` — таблица с progress-bar'ами (current / limit), period pill, isOver badge
- [ ] `src/components/RolloutPanel.astro` — timeline: in-progress / completed / failed; детальный view per rollout
- [ ] Секции встраиваются в `src/pages/ops.astro` (расширение task_094) или отдельная `src/pages/security.astro` + `quotas.astro` + `rollouts.astro`
- [ ] Add navigation entries в `Layout.astro`
- [ ] `src/hercules-web/README.md` — обновить
- [ ] `npm run build` — exit 0
- [ ] Manual smoke: после нескольких chat'ов + skill uses — quotas показывают non-zero consumption; security panel — пустой или с парой info-level событий; rollouts — пустой (или история из теста)
