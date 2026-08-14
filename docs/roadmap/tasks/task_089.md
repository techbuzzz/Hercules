# Task 89 — Capability registry panel

**Phase:** 7
**Initiative:** 48
**Status:** pending
**Owner:** —
**Slug:** `web-capability-registry-panel`

## Goal
Backend ведёт реестр известных peer-агентов через `CapabilityRegistryService` (task_034) и отдаёт по `GET /api/mesh/agents`, `GET /api/mesh/agents/{id}`, `GET /api/mesh/agents/{id}/health`, `GET /api/mesh/capabilities`, `POST /api/mesh/agents/{id}/touch`, `DELETE /api/mesh/agents/{id}`. Web не использует эти эндпойнты — нужен UI, чтобы оператор видел, какие peer'ы зарегистрированы, их capabilities, health, trust level, cost/latency hints, и мог обновить entry без CLI.

## Acceptance criteria

### Sub-tasks

- [ ] `src/lib/api.ts` — добавить `listMeshAgents(): Promise<MeshAgentEntryDto[]>`, `getMeshAgent(id): Promise<MeshAgentEntryDto>`, `getMeshAgentHealth(id): Promise<MeshAgentHealthDto>`, `listMeshCapabilities(): Promise<MeshCapabilityDto[]>`, `touchMeshAgent(id): Promise<void>`, `removeMeshAgent(id): Promise<void>`; типизировать DTO (agentId, endpoint, capabilities[], trustLevel, costHintUsd, latencyHintMs, healthStatus, lastHealthCheck, consecutiveFailures, supportedProtocolVersions[], expirySeconds)
- [ ] `src/components/CapabilityRegistryPanel.astro` — таблица с фильтрами по trust/health/capability; детальная карточка по клику; кнопки Touch / Remove с confirm-dialog
- [ ] Секция в `src/pages/memmesh.astro` (или отдельная `capabilities.astro`) — панель встраивается
- [ ] Add navigation entry в `Layout.astro` (`active?: "capabilities"`) — или скрыть под mesh-секцией
- [ ] Auto-refresh каждые 30s (signal/poll, не websocket)
- [ ] Empty state: «No known agents yet. Trigger discovery on the Mesh page.»
- [ ] `src/hercules-web/README.md` — обновить
- [ ] `npm run build` — exit 0
- [ ] Manual smoke: зарегистрировать 2 peer'а через CLI/API → web показывает обе записи с корректным health/trust
