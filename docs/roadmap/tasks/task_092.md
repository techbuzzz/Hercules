# Task 92 — Mesh profiles & backend health

**Phase:** 7
**Initiative:** 48
**Status:** done
**Owner:** —
**Slug:** `web-mesh-profiles-health`

## Goal
`MeshProfileController` (task_070) отдаёт `GET /api/mesh/profiles`, `GET /api/mesh/profiles/{name}`, `GET /api/mesh/profiles/{name}/backends`, `GET /api/mesh/backend-status`, `GET /api/mesh/backend-status/{backend}`. Web использует только mesh-метрики из `MeshController`; profile-management и live backend-health в UI не выведены. Нужна панель, которая показывает активный профиль, состояние каждого бэкенда (Healthy/Degraded/Unavailable) и degradation-policy.

## Acceptance criteria

### Sub-tasks

- [ ] `src/lib/api.ts` — добавить `listMeshProfiles()`, `getMeshProfile(name)`, `getMeshProfileBackends(name)`, `getAllBackendsStatus()`, `getBackendStatus(role)`
- [ ] Типы DTO под реальный wire format: `MeshProfileListDto` (count, profiles[], activeProfile), `MeshProfileDto` (name, description, profile, backends{role→cfg}, constraints, degradationPolicy), `MeshProfileBackendsDto` (profile, backends{role→effective}), `MeshBackendStatusListDto` (overall, backends[]), `BackendStatusDto` (role, kind, state, lastCheckedAt, consecutiveFailures, lastError)
- [ ] `src/components/MeshProfilePanel.astro` — карточка «Active profile» + список профилей с effective backends; status-grid: каждый backend (bus/queue/stateStore) с pill (Healthy/Degraded/Unavailable/Unknown), consecutiveFailures, lastCheckedAt, lastError
- [ ] Auto-refresh каждые 10s (mesh-страница) — degradation важна для оператора
- [ ] При Unavailable + policy=RefuseDelegations — баннер «Degradation policy: RefuseDelegations — new delegations blocked, see audit»
- [ ] Встраивается в `src/pages/memmesh.astro` НАД MeshDashboard
- [ ] `src/hercules-web/README.md` — обновить
- [ ] `npm run build` — exit 0
- [ ] `npx astro check` — 0 errors/warnings в новых файлах
- [x] Manual smoke: профиль `Local` → все in-process backends Healthy; переключение на `Redis` в config + restart → через 5-10s web показывает Redis Healthy; `docker stop redis` → Degraded → Unavailable за 2-3 цикла

## Implementation notes

### Round 1 (this tick) — completed

Mesh Profile & Backend Health панель для `src/pages/memmesh.astro` (Phase 7 task_092).

**API (src/lib/api.ts) — добавлены 5 методов и 6 DTO:**
- `listMeshProfiles()` → `GET /api/mesh/profiles` → `MeshProfileListDto` (count, profiles[], activeProfile)
- `getMeshProfile(name)` → `GET /api/mesh/profiles/{name}` → `MeshProfileDto` (name, description, profile, backends{role→cfg}, constraints, degradationPolicy)
- `getMeshProfileBackends(name)` → `GET /api/mesh/profiles/{name}/backends` → `MeshProfileBackendsDto` (profile, backends{role→effective})
- `getAllBackendsStatus()` → `GET /api/mesh/backend-status` → `MeshBackendStatusListDto` (overall, backends[])
- `getBackendStatus(role)` → `GET /api/mesh/backend-status/{role}` → `BackendStatusDto`

**Расхождение DTO со spec'ом:**
Спека перечисляла `BackendStatusDto` с полями `(kind, enabled, isHealthy, healthState, lastCheckedAt, latencyMs, lastError)`. Реальный wire-формат контроллера (`MeshProfileController.BackendHealthDto`) использует `(role, kind, state, lastCheckedAt, consecutiveFailures, lastError)`. `latencyMs` — это метрика peer-агента, не backend'а, и в backend-эндпоинте для бэкендов её нет; `enabled` живёт на `MeshProfileBackendDto` (конфиг), а не на health-эндпоинте. DTO в `api.ts` зеркалит реальный wire-format, чтобы избежать полей-призраков; в комментариях отмечено расхождение.

**Компонент (src/components/MeshProfilePanel.astro) — новый:**
- 4 секции:
  1. **Active profile** — name, profile-kind pill (Local/Redis/Nats/Postgres/Hybrid), description; 3 карточки effective backends (bus/queue/stateStore) с kind-pill, enabled/disabled badge, настройками healthCheck/timeout/retry; constraints (maxAgents/region/requiredServices); degradation policy (mode/maxDegradedSeconds/webhook).
  2. **Live backend health** — overall pill + grid карточек по каждому backend'у: role, kind, state pill (Healthy/Degraded/Unavailable/Unknown), consecutiveFailures (подсветка при >0), lastCheckedAt, lastError (если есть).
  3. **Все профили** — active выделен violet, остальные — inactive; description + profile kind + degradation mode.
  4. **Degradation banner** — появляется в двух режимах:
     - `RefuseDelegations` + any Unavailable → красный: «new delegations blocked, see audit».
     - `DegradeToLocal` + any Degraded/Unavailable → жёлтый: «используется in-process fallback».
- Auto-refresh каждые **10s** (opt-in чекбокс, как в `CapabilityRegistryPanel`, но короче интервал — degradation важна для оператора).
- Error banner для частичных сбоев (effective backends / health могут падать независимо).
- Robust загрузка: `Promise.all` для параллельных `getMeshProfile` (active + non-active), каждый с `.catch(() => null)`. Если active profile fetch падает — продолжаем с пустым `lastActiveProfile`, но остальные секции рендерятся (не падаем).
- Стилистика соответствует `CapabilityRegistryPanel`/`DiscoveryPanel`: тот же тёмный `#0a0a0b` фон, те же pill-палитры (emerald/sky/amber/rose/violet), `mono` шрифт для идентификаторов.

**Интеграция (src/pages/memmesh.astro):**
- `<MeshProfilePanel />` подключён в `mb-6` блоке ПЕРЕД `<MeshDashboard />` — спека требует «над MeshDashboard».

**README (src/hercules-web/README.md):**
- В «Структура» добавлен `MeshProfilePanel.astro`.
- В «Backend API» добавлены 5 mesh-profile-эндпоинтов.

### Round 1 — validation

- `npm run build` → **exit 0**, 7 страниц собрано (`/`, `/a2a`, `/config`, `/memmesh`, `/profile`, `/skills`, `/stats`). Script-бандл `MeshProfilePanel.astro_astro_type_script_index_0_lang.Ce2nVIkO.js` ≈ 9.5 KB.
- `npx astro check` → 0 errors, 0 warnings, 0 hints в `MeshProfilePanel.astro`, `memmesh.astro`, `lib/api.ts`. Pre-existing 17 errors в `MeshRouterPanel.astro` (вне scope task_092; задокументировано в task_088/089).
- Проверка `dist/memmesh/index.html`: `id="mesh-profile-panel"` присутствует ДО `id="mesh-dashboard"`, все секции (active-profile / health-grid / profiles-list) отрендерены, script-тег ведёт на bundle.

### Notes

- `enable` поле для backends берётся из `MeshProfileBackendsDto` (effective config), а не из health-эндпоинта — соответствует backend wire-формату.
- Health-эндпоинт `/api/mesh/backend-status` возвращает 404 для роли, которой нет в активном профиле. В UI панель не делает прямых вызовов per-role — она получает всё через `getAllBackendsStatus()` для согласованного overall-индикатора. Per-role endpoint оставлен в `api.ts` для будущих use-cases (например, per-backend drill-down).
- Degradation banner'ы: «RefuseDelegations» — критичный, красный; «DegradeToLocal» — информирующий, жёлтый. Если все healthy — оба скрыты.
- 10s auto-refresh выбран как разумный компромисс: degradation важна, но слишком частые reflash'и нагружают API. Opt-in чекбокс, чтобы не нагружать backend в дефолте.
