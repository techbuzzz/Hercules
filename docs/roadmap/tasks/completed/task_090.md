# Task 90 — Discovery panel

**Phase:** 7
**Initiative:** 48
**Status:** done
**Owner:** —
**Slug:** `web-discovery-panel`

## Goal
`DiscoveryService` (task_038) объединяет три источника: `StaticDiscoverySource` (config), `RegistryDiscoverySource` (capability-registry re-export), `MdnsDiscoverySource` (mDNS/Bonjour). Web не показывает, какие источники активны, какие peer'ы найдены, и не позволяет вручную триггернуть refresh. Нужна панель на mesh-странице.

## Acceptance criteria

### Sub-tasks

- [x] `src/lib/api.ts` — добавить `listDiscoverySources(): Promise<DiscoverySourceDto[]>`, `listDiscoveredAgents(source?): Promise<DiscoveredAgentDto[]>`, `refreshDiscovery(): Promise<RefreshDiscoveryResultDto>` (refreshedAt + added/removed counters).
- [x] Типы: `DiscoverySourceDto` (kind, name, enabled) — бэкенд не отдаёт lastRunAt/lastError per-source, оставляем только то, что есть в `MeshController.ListDiscoverySources`. `DiscoveredAgentDto` (agentId, displayName, endpoint, source, discoveredAt, capabilities[], manifestLoaded, error). `RefreshDiscoveryResultDto` (status, count, refreshedAt, added, removed, agents[]).
- [x] `src/components/DiscoveryPanel.astro` — секция «Discovery sources» (kind pill + enabled/disabled, mDNS-not-supported подсветка) + секция «Discovered agents» (таблица: displayName / agentId / source pill / discoveredAt relative / capabilities count / manifestLoaded icon / error inline). Кнопка «Refresh now» с post-refresh toast «+N / −M agents».
- [x] Встраивается в `src/pages/memmesh.astro` как новая секция над `<CapabilityRegistryPanel />`.
- [x] Refresh-diff в клиенте: сохраняем `Set<agentId>` до вызова, после — `added = new − old`, `removed = old − new`. `refreshedAt = new Date().toISOString()` (бэкенд не отдаёт это поле явно).
- [x] mDNS stub статус: если в списке источников есть kind=mdns и enabled=false — отображаем отдельный inline-warning «mDNS disabled on this platform» (а не общий disabled-pill).
- [x] `src/hercules-web/README.md` — обновить (добавить `DiscoveryPanel.astro` + 3 discovery-эндпоинта).
- [x] `npm run build` — exit 0; `npx astro check` — 0 errors в новых файлах.

## Implementation notes

### Round 1 (this tick) — completed

Web-UI для Discovery-механизмов: визуализация активных источников (static / registry / mDNS), просмотр
discovered peer-агентов с фильтрами, force-refresh с подсчётом +N / −M delta.

- `src/lib/api.ts` — добавлены типы `DiscoverySourceKind` (`"static" | "registry" | "mdns"`),
  `DiscoverySourceDto` (kind, source, name, enabled), `DiscoveredAgentDto` (agentId, displayName,
  endpoint, source, discoveredAt, capabilities[], manifestLoaded, error), `RefreshDiscoveryRawDto`.
  Методы: `api.listDiscoverySources()`, `api.listDiscoveredAgents(source?)`,
  `api.refreshDiscovery()`. Refresh возвращает raw payload — added/removed counters
  считаются на клиенте, потому что backend-controller (`MeshController.cs:504-526`) их не отдаёт.
- `src/components/DiscoveryPanel.astro` — новый клиентский компонент:
  - Header с timestamp-обновления, opt-in авто-refresh (60s), кнопка «Refresh now».
  - Banners: error (rose), success toast (emerald, 4s auto-hide), mDNS-not-supported (amber, persistent).
  - Секция «Источники» — pill для каждого kind с цветовой палитрой (static=sky, registry=violet,
    mdns=emerald) и enabled/disabled badge.
  - Секция «Discovered agents» — таблица (displayName / agentId / endpoint / source pill /
    discoveredAt relative / capabilities count / manifestLoaded badge / error inline с truncate).
  - Фильтры: free-text search (agent / display / endpoint / capabilities), source-kind select, error-presence select.
- `src/pages/memmesh.astro` — добавлен `<DiscoveryPanel />` между EscalationPanel-row и `<CapabilityRegistryPanel />`.
  Сборка подтверждает порядок: discovery-panel идёт ПЕРЕД capability-registry-panel.
- `src/hercules-web/README.md` — обновлены разделы «Структура» (добавлен `DiscoveryPanel.astro`)
  и «Backend API» (3 новых эндпоинта: `/api/mesh/discovery/sources`, `/agents`, `/refresh`).

### Round 1 — validation

- `npm run build` → **exit 0**, 7 страниц собраны, бандл
  `DiscoveryPanel.astro_astro_type_script_index_0_lang.D_iVdG3J.js` (6.7 KB) сгенерирован.
- `npx astro check` → 0 errors / 0 warnings / 0 hints в `DiscoveryPanel.astro`, `memmesh.astro`,
  `lib/api.ts`. Pre-existing 17 errors в `MeshRouterPanel.astro` — вне scope task_090, задокументированы
  в task_088 (там же отмечены).
- `dist/memmesh/index.html` — секции (sources / filters / agents table) и `<script>`-тег
  с bundle присутствуют. Подтверждено через `Select-String`.

### Manual smoke

Без запущенного backend нельзя выполнить end-to-end (refresh + render agents), но:
- Backend-эндпоинты зарегистрированы: `GET /api/mesh/discovery/sources`,
  `GET /api/mesh/discovery/agents?source=...`, `POST /api/mesh/discovery/refresh`
  (см. `MeshController.cs:MapMesh`).
- TypeScript DTO соответствует wire-формату: kind/source как `lowercase string`, agents[].source тоже
  lowercase, capabilities — `string[]`, error — `string | null`.
- `dist/_astro/DiscoveryPanel.*.js` собран и подключён к memmesh-странице.
