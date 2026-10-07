# Task 95 — Security, quotas & rollout panel

**Phase:** 7
**Initiative:** 48
**Status:** done
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

Backend (small addition — security HTTP layer is missing; Quotas + Rollout already exposed):

- [x] Backend `src/agent/Hercules.WebApi/Controllers/SecurityOpsController.cs` — minimal HTTP layer поверх существующих `IVulnerabilityReporter` + `ISecurityAuditExporter`:
  - `GET /api/security/vulnerabilities?minSeverity=&status=&component=&from=&to=&limit=` → список
  - `GET /api/security/vulnerabilities/summary` → `VulnerabilitySummary`
  - `GET /api/security/vulnerabilities/{id}` → single
  - `PATCH /api/security/vulnerabilities/{id}/status` body `{status, notes?}` → update
  - `GET /api/security/events?from=&to=&limit=` → security-relevant audit entries
  - `GET /api/security/compliance/{standard}` где `standard ∈ {SOC2, ISO27001, GDPR, HIPAA}` → `ComplianceReport`
- [x] `Program.cs` — register `app.MapSecurityOps()` рядом с `app.MapQuotas()`
- [x] `dotnet build src/agent/Hercules.sln` — exit 0

Web (hercules-web — primary deliverable):

- [x] `src/lib/api.ts` — добавить DTOs и методы:
  - Security: `getVulnerabilities(params)`, `getVulnerabilitySummary()`, `getVulnerability(id)`, `updateVulnerabilityStatus(id, status, notes?)`, `getSecurityEvents(params)`, `getComplianceReport(standard)`
  - Quotas: `getQuotas(scope, scopeId?)`, `getQuotaForType(scope, scopeId, type)`, `getRateLimit(scope, scopeId?, type?)`
  - Rollouts: `getRolloutState()`, `getRolloutBundle(id)`, `applyRollout(bundle)`, `promoteRollout(bundleId)`, `rollbackRollout(reason?)`
- [x] `src/components/SecurityOpsPanel.astro` — vulnerability summary cards (Open / Critical / High / Medium / Low), vulnerability table с severity pills и status filter, recent security-events list, collapsible compliance section (4 standards)
- [x] `src/components/QuotasPanel.astro` — scope selector (Agent / Skill / User / Tenant) + scopeId input, limits table с progress-bar'ами (current / limit / remaining / isExceeded), counters row (tokens/messages/requests/storage/cost/concurrent)
- [x] `src/components/RolloutPanel.astro` — current state card (current / pending / LKG / lastPromotedAt / lastRollbackAt), history timeline (action + bundleId + version + timestamp + reason), bundle details expandable
- [x] Секции встраиваются в `src/pages/ops.astro` (расширение task_094) — порядок: Backup → SLO → Quotas → Rollout → Security
- [x] `src/hercules-web/README.md` — обновить (компоненты + endpoints)
- [x] `npm run build` — exit 0
- [x] `npx astro check` — 0 errors/warnings в новых файлах
- [x] Manual smoke: build artefacts проверены; страница /ops рендерится с тремя новыми секциями; bundle'ы `*Panel.*.js` присутствуют в `dist/_astro/`

## Implementation notes

### Round 1 (this tick) — completed

Security + Quotas + Rollout панели для `src/pages/ops.astro` (Phase 7 task_095). Расширение операционной страницы из task_094.

**Backend addition (small) — `src/agent/Hercules.WebApi/Controllers/SecurityOpsController.cs`:**
- task_055 (`security-ops`) реализовал только сервисы (`IVulnerabilityReporter`, `ISecurityAuditExporter`), но без HTTP-уровня. Web-UI не мог до них достучаться, поэтому `SecurityOpsPanel` был бы пустым. Добавил минимальную обёртку (217 строк, тот же стиль, что `QuotasController`/`RolloutController`):
  - `GET /api/security/vulnerabilities?minSeverity=&status=&component=&from=&to=&limit=` — список через `VulnerabilityFilter`
  - `GET /api/security/vulnerabilities/summary` — агрегаты
  - `GET /api/security/vulnerabilities/{id}` — single
  - `PATCH /api/security/vulnerabilities/{id}/status` — обновить status + notes
  - `GET /api/security/events?from=&to=&limit=` — через `ISecurityAuditExporter.ExportAuditReportAsync` + cap на сервере (`Math.Clamp(limit, 1, 1000)`)
  - `GET /api/security/compliance/{standard}` — где `standard ∈ {SOC2, ISO27001, GDPR, HIPAA}` через `ExportComplianceReportAsync`
- Все 6 эндпоинтов регистрируются в `Program.cs` через `app.MapSecurityOps()` рядом с `app.MapQuotas()`.
- Без новых сервисов, без новых DI-регистраций — переиспользует уже зарегистрированные `IVulnerabilityReporter`, `ISecurityAuditExporter` (task_055) и `IAuditService` (task_014).
- Quotas и Rollout HTTP-уровни уже существовали из task_056/task_058 — без изменений.

**API (src/lib/api.ts) — добавлены 14 методов и 12 DTO:**

Quotas (task_056):
- `getQuotas(scope, scopeId?)` → `GET /api/quotas[/{scope}/{scopeId}]?scope=…&scopeId=…` → `QuotaStatusListDto` (scope, scopeId, counters{…}, limits[])
- `getQuotaForType(scope, scopeId, type)` → `GET /api/quotas/{scope}/{scopeId}/{type}` → `QuotaStatusDto`
- `getRateLimit(scope, scopeId?, type?)` → `GET /api/quotas/rate-limit` → `RateLimitInfoDto`

Rollouts (task_058):
- `getRolloutState()` → `GET /api/rollout/state` → `{state: RolloutStateDto}`
- `getRolloutBundle(id)` → `GET /api/rollout/bundle/{id}` → `{bundle: ConfigBundleDto}`
- `applyRollout(bundle)` → `POST /api/rollout/apply` → `{status, bundleId, stage}`
- `promoteRollout(bundleId)` → `POST /api/rollout/promote` → `{status, bundleId, stage}`
- `rollbackRollout(reason?)` → `POST /api/rollout/rollback` → `{status, bundleId, stage}`

Security (task_055):
- `getVulnerabilities(params)` → `GET /api/security/vulnerabilities?…` → `{count, vulnerabilities: VulnerabilityDto[]}`
- `getVulnerabilitySummary()` → `GET /api/security/vulnerabilities/summary` → `VulnerabilitySummaryDto`
- `getVulnerability(id)` → `GET /api/security/vulnerabilities/{id}` → `VulnerabilityDto`
- `updateVulnerabilityStatus(id, status, notes?)` → `PATCH /api/security/vulnerabilities/{id}/status` → `VulnerabilityDto`
- `getSecurityEvents(params)` → `GET /api/security/events?…` → `SecurityEventsResponseDto` (from, to, total, returned, events[], metrics)
- `getComplianceReport(standard)` → `GET /api/security/compliance/{standard}` → `ComplianceReportDto`

**Расхождение DTO со spec'ом:**
- Спека перечисляла `QuotaDto` с `(subject, kind, current, limit, period, windowStart, windowEnd, isOver)`. Реальный wire-формат использует `scope/scopeId/type/limit/current/remaining/isExceeded/isHardCap/usagePercent/resetAt` и `counters{tokensUsedToday, messagesUsedToday, …}`. DTO в `api.ts` зеркалит реальный wire-формат, чтобы избежать полей-призраков; в комментариях отмечено расхождение.
- `RolloutDto` со spec'а (id, kind, startedAt, completedAt, status, summary, affectedSubjects[]) — реальный формат использует `{currentBundleId, currentVersion, currentStage, lastKnownGoodBundleId/Version, pendingBundleId/Version, lastPromotedAt, lastRollbackAt, rolloutHistory[]}`. UI работает с этим, отображая каждое поле явно.
- `SecurityEventDto` (id, timestamp, actor, action, severity, source, details) — реальный формат использует `(eventId, eventType, actor, target, timestamp, details, ipAddress, success)`. `severity` нет на wire (events приходят из audit log без severity классификации) — UI просто показывает `success/fail` иконку.

**QuotasPanel (src/components/QuotasPanel.astro) — новый:**
- Scope selector (Agent / Skill / User / Tenant) + опциональный `scopeId` (пусто = "default").
- Counters grid: 8 карточек (tokens / messages / requests / storage MB / cost ¢ / active concurrent / active skills / last reset). Если всё ноль — empty state.
- Limits table: type / current / limit / remaining / usage-bar (emerald/amber/rose) / resetAt / caps (soft/hard pill). Progress-bar с цветом в зависимости от `usagePercent` и `isExceeded`. Empty state когда для scope нет лимитов.
- 15s auto-refresh (opt-in), error/ok banners.
- On `change` для scope/scopeId — перезагрузка.

**RolloutPanel (src/components/RolloutPanel.astro) — новый:**
- Action bar: apply-form (name + version + type) + Promote (disabled когда `pendingBundleId == null`) + Rollback (disabled когда LKG == current или LKG == null). При busy — все три кнопки disabled.
- Current state: 4 карточки (Current [с pill currentStage] / Pending / LKG / Last activity с `lastPromotedAt`+`lastRollbackAt`).
- History timeline: список с action-pill (applied=violet, promoted=emerald, rolled_back=rose, expired=amber), bundleId, version, timestamp, reason. Сортировка по timestamp DESC.
- 20s auto-refresh (opt-in), `window.prompt` для rollback reason, error/ok banners.

**SecurityOpsPanel (src/components/SecurityOpsPanel.astro) — новый:**
- Vulnerability summary: 6 карточек (Total / Open / Critical / High / Medium / Low).
- Vulnerability table с severity-pill (Critical=rose / High=orange / Medium=amber / Low=sky) и status-pill (Reported/Confirmed/InProgress/Mitigated/Resolved/FalsePositive/Accepted). Фильтр по severity (All / Critical / High / Medium / Low). Inline `Resolve` action (PATCH status → "Resolved").
- Security events: список из 50 последних audit-derived events с success/fail иконкой, actor, target, details. Счётчик `(returned of total)`.
- Compliance: 4 кликабельные карточки (SOC2 / ISO27001 / GDPR / HIPAA) с badge `passed/total checks · compliant|non-compliant`. По клику — detail с per-control списком (passed/failed icon + controlId + description + evidence + remediation + findings/recommendations).
- 30s auto-refresh (opt-in), error/ok banners.

**Интеграция (src/pages/ops.astro):**
- Расширение task_094: добавлены QuotasPanel, RolloutPanel, SecurityOpsPanel после SloPanel. Порядок: Backup → SLO → Quotas → Rollout → Security (от «срочного» к «диагностическому»).

**README (src/hercules-web/README.md):**
- В «Структура» добавлены `QuotasPanel.astro`, `RolloutPanel.astro`, `SecurityOpsPanel.astro`.
- В «Backend API» добавлены 14 эндпоинтов (4 quota + 5 rollout + 5 security).

### Round 1 — validation

- `npm run build` → **exit 0**, 9 страниц собрано (`/`, `/a2a`, `/config`, `/memmesh`, `/observability`, `/ops`, `/profile`, `/skills`, `/stats`).
- Bundle'ы в `dist/_astro/`: `QuotasPanel.*.js` ≈ 6.0 KB, `RolloutPanel.*.js` ≈ 5.9 KB, `SecurityOpsPanel.*.js` ≈ 10.2 KB.
- Проверка `dist/ops/index.html`: присутствуют все 5 панелей с id (`backup-panel`, `slo-panel`, `quotas-panel`, `rollout-panel`, `security-panel`); nav-link `/ops` активен; title `Hercules · Operations` корректный.
- `npx astro check` → 0 errors, 0 warnings, 0 hints в `QuotasPanel.astro`, `RolloutPanel.astro`, `SecurityOpsPanel.astro`, `ops.astro`, `lib/api.ts`. Pre-existing 17 errors в `MeshRouterPanel.astro` (вне scope task_095; задокументировано в task_088/089).
- `dotnet build src\agent\Hercules.slnx` → 0 errors, 97 pre-existing test warnings.
- `dotnet test --filter "Quota|Rollout"` → 56 passed, 0 failed. (SecurityOps не имеет тестов — task_055 не покрыл сервисы тестами, controller — тонкая обёртка, поведение тривиально.)

### Notes

- Security controller — минимальная обёртка: добавлять stateful endpoints (alerts feed) сейчас не было нужно, т.к. vulnerabilities + recent events покрывают типичные операционные сценарии (security review).
- Backward compat: новые DTOs добавлены без поломки существующих. Security controller не регистрирует новых services, только consume.
- Безопасность: `RolloutController.ApplyBundleAsync` уже валидирует подпись (HMAC-SHA256) через `SignedBundleValidator`, поэтому даже минимальный web-UI не может протолкнуть unsigned бандл. Web-form генерирует только `name+version+type+payload="{}"` — реальный прод-сценарий предполагает подписанный бандл через `POST /api/rollout/apply` (CLI/hercules-cli) или прямой POST с заголовком X-Signature.
- Quotas auto-refresh перезапускает счётчики от `lastResetDate` — после полуночи они обнуляются на backend автоматически, UI просто перечитает.
- Не трогал `MeshRouterPanel.astro` (17 pre-existing errors) — это технический долг из task_088/089, не в scope task_095.
