# Stage 4 — Mesh Explorer

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 2 недели
**Dependencies (backend):** нет
**Dependencies (Studio):** Stage 1

## Goal

Визуальная карта mesh (Vue Flow): topology graph, node details, router explorer, shared memory browser, circuit breaker panel, auto-refresh 30s.

## Tasks

### 4.1 — Mesh store

- [ ] `stores/mesh.ts`:
  - `agents: MeshAgentDto[]` — from `GET /api/mesh/agents`
  - `dashboard: MeshDashboardDto` — from `GET /api/mesh/dashboard`
  - `topology: MeshTopologyDto`
  - `health: MeshHealthDto`
  - `circuits: Record<string, string>` — from `GET /api/mesh/circuits`
  - `routerHealth: MeshRouterHealthDto`
  - `sharedMemory: SharedFact[]`
  - `denials: MeshPolicyDenialsDto`
  - `heatmap: MeshSkillHeatmapDto`
  - `evalSummary: MeshEvalSummaryDto`
  - `load()` — fetch all in parallel
  - `refresh()` — re-fetch (auto 30s)
  - `registerPeer(manifest)`, `removeAgent(id)`, `touchAgent(id)`, `cleanupAgents()`
  - `resetCircuit(id)`

### 4.2 — MeshExplorer view (Vue Flow graph)

- [ ] `views/MeshExplorer.vue`:
  - Vue Flow canvas
  - Nodes: agents from `GET /api/mesh/agents`
    - Color by health: green (healthy), amber (degraded), red (unhealthy)
    - Size by capability count
    - Label: displayName, agentId, endpoint
    - Self-agent highlighted differently (center node)
  - Edges: trust/delegation relations (from manifest peers)
  - Layout: force-directed (dagre or custom)
  - Zoom, pan, fit-to-view
  - Auto-refresh 30s + manual "Refresh" button
  - Legend: health colors, node sizes

### 4.3 — Node details panel

- [ ] `components/mesh/NodeDetailsPanel.vue`:
  - Shown on node click (sidebar or right panel)
  - Fields: agentId, displayName, endpoint, healthScore, healthStatus, latencyMs, qualityScore, trustLevel, lastSeen, capabilities[], circuitState, consecutiveFailures
  - Capabilities list: name, description, tools
  - Skills list (from manifest)
  - Actions: touch (heartbeat), remove, circuit reset

### 4.4 — Context menu on nodes

- [ ] Right-click context menu on Vue Flow node:
  - "View details" → NodeDetailsPanel
  - "Touch (heartbeat)" → `POST /api/mesh/agents/{id}/touch`
  - "Register peer" → `POST /api/mesh/agents/register` (with manifest URL)
  - "Remove from registry" → `DELETE /api/mesh/agents/{id}`
  - "Reset circuit breaker" → `POST /api/mesh/circuits/{id}/reset`
  - "Cleanup stale agents" → `POST /api/mesh/agents/cleanup`

### 4.5 — Router explorer

- [ ] `components/mesh/RouterExplorer.vue`:
  - Search by capability: input → `GET /api/mesh/capabilities/search?phrase=...`
  - Or select capability from dropdown → `GET /api/mesh/capabilities/{name}`
  - Results table: ranked candidates (agentId, displayName, endpoint, healthScore, latencyMs, qualityScore, trustLevel, compositeScore, costHintUsd, circuitState, lastSeen, trustPassed)
  - Sort by compositeScore (default), health, latency, cost
  - Filter: trustPassed only, healthy only, circuit not open
  - "Route intent" button → opens intent form (→ Stage 7 consensus or Stage 8 workflow)

### 4.6 — Shared memory browser

- [ ] `components/mesh/SharedMemoryBrowser.vue`:
  - List shared facts: `GET /api/mesh/shared-memory`
  - Filter by agentId: `GET /api/mesh/shared-memory/for/{agentId}`
  - Publish fact: `POST /api/mesh/shared-memory` (key, value, namespace, TTL, classification)
  - Sync: `POST /api/mesh/shared-memory/sync`
  - Delete fact: `DELETE /api/mesh/shared-memory/{factId}`
  - Fact details: id, key, value, namespace, provenance, TTL, classification, createdAt

### 4.7 — Dashboard panels

- [ ] `components/mesh/MeshDashboard.vue` (tabbed):
  - **Topology** — Vue Flow graph (from 4.2)
  - **Traffic** — totalRequests, fanOutRequests, meshDelegations, avgLatencyMs, meshRouterHits, circuitBreakerRejections (from `GET /api/mesh/dashboard` traffic field)
  - **Health** — agents table with healthStatus, circuitState, consecutiveFailures, avgLatencyMs (from `GET /api/mesh/health`)
  - **Denials** — policy denial log (from `GET /api/mesh/denials`)
  - **Skill heatmap** — skills by usage/success (from `GET /api/mesh/skills/heatmap`)
  - **Eval summary** — totalRuns, passedRuns, failedRuns, recentRuns table (from `GET /api/mesh/eval/summary`)

### 4.8 — Circuit breaker panel

- [ ] `components/mesh/CircuitBreakerPanel.vue`:
  - List circuits: `GET /api/mesh/circuits` → `{agentId: state}`
  - State badges: closed (green), open (red), half-open (amber)
  - Reset button per circuit: `POST /api/mesh/circuits/{id}/reset`

### 4.9 — Sidebar: Mesh activity

- [ ] Sidebar for "Mesh" activity:
  - Dashboard sub-nav: Topology, Router, Shared Memory, Circuits, Denials, Heatmap, Evals
  - Click → opens corresponding panel in MainWorkbench

### 4.10 — Tests

- [ ] Unit: mesh store load/refresh, router explorer search
- [ ] E2E: open mesh explorer → see graph → click node → details → router search → results

## Acceptance criteria

- [ ] MeshExplorer: Vue Flow graph with agent nodes, health colors, size by capabilities
- [ ] Node click → details panel with all fields
- [ ] Context menu: touch, remove, register, circuit reset, cleanup
- [ ] Auto-refresh 30s + manual button
- [ ] Router explorer: search by capability → ranked candidates table with scores
- [ ] Shared memory: list, publish, sync, delete
- [ ] Dashboard: 6 tabs (topology, traffic, health, denials, heatmap, evals)
- [ ] Circuit breaker: list with states, reset per circuit
- [ ] `npm run build` + tests pass

## Scope / Likely files

- `renderer/src/views/MeshExplorer.vue`
- `renderer/src/components/mesh/` (NodeDetailsPanel, RouterExplorer, SharedMemoryBrowser, MeshDashboard, CircuitBreakerPanel)
- `renderer/src/stores/mesh.ts`

## Dependencies

- **Backend:** нет (текущий mesh API: /api/mesh/agents, /dashboard, /router, /shared-memory, /circuits)
- **Studio:** Stage 1 (connection manager)

## Risks / Rollback

- **Vue Flow performance:** many agents (50+) may lag. Mitigation: virtualization, simplify node rendering.
- **1-hop only:** peers-of-peers not shown. Mitigation: documented, multi-hop later.
- **Edge data:** mesh API may not explicitly return trust edges. Mitigation: infer from manifest.peers or shared-memory provenance.

## Links

- Epic: [../README.md](../README.md)
- Stage 1: [stage_01_agent_scanner.md](stage_01_agent_scanner.md)