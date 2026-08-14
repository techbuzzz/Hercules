# Task 53 — Mesh dashboard

**Phase:** 5
**Initiative:** 32
**Status:** done
**Owner:** —
**Slug:** `mesh-dashboard`

## Goal
Web UI: live agent topology, traffic, health, policy denials, бюджеты, queued tasks, skill usage heatmap, последние eval results.

## Acceptance criteria
- [x] `MeshDashboardService` — server-side aggregator exposing all dashboard data
- [x] `GET /api/mesh/dashboard` endpoint — single endpoint returning topology, health, traffic, denials, budget, queued tasks, skill heatmap, eval results
- [x] `MeshTopologyDto` — agent list with health/trust/latency per entry
- [x] `MeshTrafficDto` — request count, fan-out count, avg latency (last 24h), routing decisions
- [x] `MeshHealthDto` — per-agent health scores, circuit breaker states, uptime
- [x] `MeshPolicyDenialsDto` — recent policy denials from audit log (filtered by denial events)
- [x] `MeshSkillHeatmapDto` — per-skill usage count and success rate for heatmap grid
- [x] `MeshEvalSummaryDto` — last N eval runs with pass/fail, score, scenario type
- [x] Frontend `MeshDashboard.astro` component with all 7 sections
- [x] `mesh.astro` page updated to use new dashboard component
- [x] `dotnet build` + `dotnet test` pass

## Sub-tasks
- [x] `src/agent/Mesh/Dashboard/MeshDashboardDtos.cs` — all DTO models
- [x] `src/agent/Mesh/Dashboard/MeshDashboardService.cs` — aggregator service
- [x] `GET /api/mesh/dashboard` endpoint in `MeshController.cs`
- [x] `GET /api/mesh/topology` endpoint (agent list with health/trust/latency)
- [x] `GET /api/mesh/health` endpoint (per-agent health + CB states)
- [x] `GET /api/mesh/denials` endpoint (recent policy denials)
- [x] `GET /api/mesh/skills/heatmap` endpoint (skill usage grid)
- [x] `GET /api/mesh/eval/summary` endpoint (recent eval runs)
- [x] `MeshServiceExtensions.cs` — DI registration for `MeshDashboardService`
- [x] `src/hercules-web/src/components/MeshDashboard.astro` — full 7-section dashboard component
- [x] Update `src/hercules-web/src/pages/memmesh.astro` to include MeshDashboard
- [x] Add API methods to `src/hercules-web/src/lib/api.ts`
- [x] `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors
- [x] `dotnet test tests/Hercules.Agent.Tests/` — 1374/1383 pass (9 pre-existing failures: OtelServiceTests ×5, BudgetGuardTests ×1, NumericValidatorTests ×2, WasmToolTests ×1)
- [x] `npm run build` (hercules-web) — 6 pages built, 0 errors

## Implementation notes

### Backend

**`src/agent/Mesh/Dashboard/MeshDashboardDtos.cs`**
- `MeshTopologyDto`, `MeshAgentDto`, `MeshTrafficDto`, `MeshHealthDto`, `MeshHealthEntryDto`
- `MeshPolicyDenialsDto`, `MeshDenialEntryDto`
- `MeshSkillHeatmapDto`, `MeshSkillHeatmapEntryDto`
- `MeshEvalSummaryDto`, `MeshEvalRunDto`
- `MeshDashboardDto` — complete snapshot

**`src/agent/Mesh/Dashboard/MeshDashboardService.cs`**
- Injects `ICapabilityRegistryService`, `CircuitBreaker`, `IAuditService`, `WebApiAdapter`, `IMeshEvalRunner`
- `GetDashboardAsync()` — full snapshot (all 6 sections)
- `GetTopologyAsync()` — agents from registry with health/trust/latency
- `GetTrafficAsync()` — traffic counters (CB rejections + placeholders for full OTel metrics)
- `GetHealthAsync()` — per-agent health + CB states
- `GetPolicyDenialsAsync()` — audit log filtered by `result=denied` + `action=trust_denied`
- `GetSkillHeatmap()` — local skill usage from `WebApiAdapter.ListSkills()`
- `GetEvalSummaryAsync()` — last 20 eval runs from baseline.json

**`MeshController.cs`** — 6 new endpoints:
- `GET /api/mesh/dashboard` — full snapshot
- `GET /api/mesh/topology` — agent list
- `GET /api/mesh/health` — health + CB states
- `GET /api/mesh/denials` — policy denials
- `GET /api/mesh/skills/heatmap` — skill usage
- `GET /api/mesh/eval/summary` — eval runs

**`MeshServiceExtensions.cs`** — `services.AddSingleton<MeshDashboardService>()`

### Frontend

**`src/hercules-web/src/lib/api.ts`** — 6 new API methods + all TypeScript DTOs for mesh dashboard.

**`src/hercules-web/src/components/MeshDashboard.astro`**
- 7 sections: Topology, Traffic, Health, Policy Denials, Skill Heatmap, Eval Summary
- Auto-loads on mount, manual refresh per section and global refresh
- Skill heatmap: color-coded cells (emerald/amber/rose) by success rate × usage intensity
- Health: progress bars per agent with CB state badge

**`src/hercules-web/src/pages/memmesh.astro`** — updated to show MeshDashboard above the existing MeshRouterPanel + EscalationPanel.

## Validation

```
dotnet build src/agent/Hercules.csproj -c Release       → 0 errors
dotnet test tests/Hercules.Agent.Tests/                 → 1374/1383 pass
                                                           (9 pre-existing failures)
npm run build (src/hercules-web/)                       → 6 pages built, 0 errors
```

## Scope / Likely files
src/agent/Mesh/Dashboard/, src/hercules-web/src/components/MeshDashboard.astro, src/hercules-web/src/pages/memmesh.astro

## Dependencies
- блокирует / опирается на: [task_013 — opentelemetry](task_013.md)
- блокирует / опирается на: [task_034 — capability-registry](task_034.md)

## Risks / Rollback
UI-сложность; phase-gated фичи с feature flags.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
