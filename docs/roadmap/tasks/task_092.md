# Task 92 — Mesh profiles & backend health

**Phase:** 7
**Initiative:** 48
**Status:** pending
**Owner:** —
**Slug:** `web-mesh-profiles-health`

## Goal
`MeshProfileController` (task_070) отдаёт `GET /api/mesh/profiles`, `GET /api/mesh/profiles/{name}`, `GET /api/mesh/profiles/{name}/backends`, `GET /api/mesh/backend-status`, `GET /api/mesh/backend-status/{backend}`. Web использует только mesh-метрики из `MeshController`; profile-management и live backend-health в UI не выведены. Нужна панель, которая показывает активный профиль, состояние каждого бэкенда (Healthy/Degraded/Unavailable) и degradation-policy.

## Acceptance criteria

### Sub-tasks

- [ ] `src/lib/api.ts` — добавить `listMeshProfiles(): Promise<MeshProfileDto[]>`, `getMeshProfile(name): Promise<MeshProfileDto>`, `getMeshProfileBackends(name): Promise<MeshProfileBackendsDto>`, `getAllBackendsStatus(): Promise<BackendStatusDto[]>`, `getBackendStatus(kind: string): Promise<BackendStatusDto>`
- [ ] Типы: `MeshProfileDto` (name, description, backends[], constraints[], degradationPolicy), `MeshProfileBackendsDto` (name, backends[]), `BackendStatusDto` (kind, enabled, isHealthy, healthState, lastCheckedAt, latencyMs, lastError)
- [ ] `src/components/MeshProfilePanel.astro` — карточка «Active profile» + список всех профилей с backends; status-grid: каждый бэкенд (Local/Redis/Nats/Postgres) с pill (Healthy/Degraded/Unavailable), latency, lastCheckedAt, lastError (если есть)
- [ ] Auto-refresh каждые 10s (mesh-страница) — degradation важна для оператора
- [ ] При Unavailable — баннер «Degradation policy: DegradeToLocal — new delegations blocked, see audit» (если policy=RefuseDelegations)
- [ ] Встраивается в `src/pages/memmesh.astro` над MeshDashboard
- [ ] `src/hercules-web/README.md` — обновить
- [ ] `npm run build` — exit 0
- [ ] Manual smoke: профиль `Local` → только Local бэкенд Healthy; переключение на `Redis` в config + restart → через 5-10s web показывает Redis Healthy; `docker stop redis` → Redis → Degraded → Unavailable за 2-3 цикла health-check'а
