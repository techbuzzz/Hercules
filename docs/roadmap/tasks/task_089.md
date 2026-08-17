# Task 89 — Capability registry panel

**Phase:** 7
**Initiative:** 48
**Status:** done
**Owner:** —
**Slug:** `web-capability-registry-panel`

## Goal
Backend ведёт реестр известных peer-агентов через `CapabilityRegistryService` (task_034) и отдаёт по `GET /api/mesh/agents`, `GET /api/mesh/agents/{id}`, `GET /api/mesh/agents/{id}/health`, `GET /api/mesh/capabilities`, `POST /api/mesh/agents/{id}/touch`, `DELETE /api/mesh/agents/{id}`. Web не использует эти эндпойнты — нужен UI, чтобы оператор видел, какие peer'ы зарегистрированы, их capabilities, health, trust level, cost/latency hints, и мог обновить entry без CLI.

## Acceptance criteria

### Sub-tasks

- [x] `src/lib/api.ts` — добавить `listMeshAgents(): Promise<MeshAgentEntryDto[]>` (распаковывает `{count, agents}` ответа), `getMeshAgent(id): Promise<MeshAgentEntryDto>`, `getMeshAgentHealth(id): Promise<MeshAgentHealthDto>`, `listMeshCapabilities(agentId?): Promise<MeshCapabilityDto[]>` (для конкретного агента), `searchMeshByCapability(phrase): Promise<MeshAgentEntryDto[]>`, `touchMeshAgent(id): Promise<void>`, `removeMeshAgent(id): Promise<void>`; типизировать DTO под `RegistryAgentFullEntry` (agentId, displayName, description, endpoint, lastSeen, healthStatus, lastHealthCheck, consecutiveFailures, trustLevel, costHintUsd, latencyHintMs, expirySeconds, supportedProtocolVersions) + `MeshAgentHealthDto` (consecutiveFailures, trustLevel, costHintUsd, latencyHintMs, expirySeconds) + `MeshCapabilityDto` (name, description, phraseReceivers[]).
- [x] `src/components/CapabilityRegistryPanel.astro` — таблица с фильтрами trust/health/capability (server-side capability search через `searchMeshByCapability`); детальная карточка по клику с health metadata + capabilities list; кнопки Touch / Remove с confirm-dialog.
- [x] Секция в `src/pages/memmesh.astro` — панель встраивается после существующих MeshRouterPanel/EscalationPanel.
- [x] Add navigation entry в `Layout.astro` (`active?: "capabilities"`) — добавить ссылку в шапку, visible из любой страницы.
- [x] Auto-refresh каждые 30s (opt-in чекбокс, не websocket).
- [x] Empty state: «No known agents yet. Trigger discovery on the Mesh page.» — показывается при пустом реестре.
- [x] `src/hercules-web/README.md` — обновить раздел Backend API (новые mesh/agents, mesh/agents/{id}, mesh/agents/{id}/health, mesh/agents/{id}/touch, mesh/agents/{id} DELETE, mesh/capabilities, mesh/capabilities/{name}, mesh/capabilities/search) и структуру (CapabilityRegistryPanel.astro).
- [x] `npm run build` — exit 0.
- [x] `npx astro check` — 0 errors, 0 warnings в новых файлах.
- [x] Manual smoke: dev-build собирает страницу mesh с новой панелью; кнопки Touch/Remove не падают при отсутствии backend (показывают error banner, а не 500).

## Implementation notes

### Round 1 (this tick) — completed

Capability Registry web panel: реестр peer-агентов с фильтрами и CRUD-операциями.

- `src/lib/api.ts` — добавлены 7 методов и 3 DTO-интерфейса:
  - `MeshAgentEntryDto` (полное зеркало `RegistryAgentFullEntry`, camelCase, поле `supportedProtocolVersionsJson` оставлено строкой, как отдаёт backend).
  - `MeshAgentLightDto` (5 полей — для capability search и `/api/mesh/capabilities` без agentId).
  - `MeshAgentHealthDto` (5 полей — для `/api/mesh/agents/{id}/health`).
  - `MeshCapabilityDto` (name, description, phraseReceivers[] — для `/api/mesh/capabilities?agentId=…`).
  - `listMeshAgents()` распаковывает `{count, agents}` ответа `Results.Ok(new { count, agents })` в `MeshController.cs`.
  - `getMeshAgent` / `getMeshAgentHealth` / `listMeshCapabilities(agentId)` / `searchMeshByCapability(phrase)` / `touchMeshAgent` / `removeMeshAgent`.

- `src/components/CapabilityRegistryPanel.astro` — новый клиентский компонент:
  - Заголовок + «обновлено в HH:MM:SS» + чекбокс «авто (30s)» + кнопка «Обновить».
  - Фильтры: текстовый поиск (agentId/displayName/description, client-side debounce 120ms), capability (server-side через `searchMeshByCapability`, debounce 350ms), trust (unverified/sandbox/basic/trusted/privileged), health (healthy/degraded/unhealthy/unreachable/unknown).
  - При активном capability-фильтре показывается плашка «показан результат server-side поиска» + кнопка «Сбросить».
  - Таблица: Agent (displayName + agentId + endpoint), Trust badge, Health badge, Last seen (relative + absolute в `title`), Cost, Latency, Consecutive failures (подсветка при >0), Touch / Remove actions.
  - Клик по строке → детальная карточка: Identity (все поля), Health (consecutiveFailures/trustLevel/costHintUsd/latencyHintMs/expirySeconds в grid-карточках), Capabilities list (name + phraseReceivers chips).
  - Touch → `POST /api/mesh/agents/{id}/touch` + reload. Remove → `confirm()` + `DELETE /api/mesh/agents/{id}` + reload. При успехе — зелёный ok-banner (auto-hide 2.5s), при ошибке — красный error-banner.
  - Empty states: «No known agents yet. Trigger discovery on the Mesh page.» (когда реестр пуст) и «Нет агентов, удовлетворяющих фильтрам.» (когда фильтры не пропускают).
  - 30-секундный `setInterval` auto-refresh, opt-in через чекбокс.
  - При удалении активного агента из реестра — детальная карточка автоматически закрывается.

- `src/pages/memmesh.astro` — `<CapabilityRegistryPanel />` подключён после `<EscalationPanel />` в `mt-6` блоке.

- `src/layouts/Layout.astro` — `active` union расширен значением `"capabilities"`, в `links` добавлена `{ href: "/mesh#capability-registry-panel", label: "Capabilities", key: "capabilities" }` (между A2A и Профиль). Использование `href="/mesh#anchor"` — лёгкий deep-link на панель без отдельной страницы.

- `src/hercules-web/README.md` — обновлены разделы «Структура» (новый `CapabilityRegistryPanel.astro`) и «Backend API» (9 mesh-эндпойнтов: agents, agents/{id}, agents/{id}/health, agents/{id}/touch, agents/{id} DELETE, capabilities?agentId=, capabilities/{name}, capabilities/search).

### Validation

- `npm run build` → **exit 0**, 7 страниц собраны (`/`, `/a2a`, `/config`, `/memmesh`, `/profile`, `/skills`, `/stats`). Размер JS-бандла `CapabilityRegistryPanel.astro_astro_type_script_index_0_lang.*.js` ≈ 12.5 KB.
- `npx astro check` — 0 errors, 0 warnings, 0 hints в моих файлах. 17 pre-existing errors в `MeshRouterPanel.astro` (вне scope task_089; задокументированы ранее в task_088).
- Проверка `dist/memmesh/index.html`: `id="capability-registry-panel"` присутствует, секции (filters / table / detail) отрендерены, script-тег ведёт на bundle.
- Проверка `dist/index.html`: nav-link `Capabilities` присутствует в шапке.

### Notes

- Backend отдаёт `supportedProtocolVersions` как JSON-строку (`"[\"1.0\"]"`) — UI рендерит её as-is, без parse. Если в будущем понадобится typed-array, можно добавить клиентский `JSON.parse` с try/catch.
- `listMeshCapabilities()` без `agentId` отдаёт `ListAgents()` (lightweight список), поэтому в API-клиенте метод `listMeshCapabilities` принимает обязательный `agentId` — для детальной карточки. Для «все capabilities всех агентов» — это N+1 запрос, и UI делает это только при открытии деталей одного агента.
