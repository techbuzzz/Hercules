# Task 43 — Mesh router

**Phase:** 4
**Initiative:** 17
**Status:** done
**Owner:** —
**Slug:** `mesh-router`

## Goal
Когда локальный навык недоступен, неприменим или ниже confidence threshold, router выбирает лучший trusted peer по capability, policy, health, latency, quality score и budget.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/Router/MeshRouterOptions.cs` — config: `Enabled`, `FallbackToPeer`, `MaxPeerCandidates`, `DefaultTimeoutMs`, `MinConfidenceThreshold`
- [x] `src/agent/Mesh/Router/PeerCandidate.cs` — model: `AgentId`, `Capabilities`, `HealthScore`, `LatencyMs`, `QualityScore`, `TrustLevel`, `LastSeen`
- [x] `src/agent/Mesh/Router/IMeshRouter.cs` — interface: `RouteAsync(intent, capability, budget, ct) → PeerCandidate[]` with ranked results
- [x] `src/agent/Mesh/Router/CapabilityMeshRouter.cs` — full implementation: query `ICapabilityRegistry`, filter by `ITrustPolicy`, rank by weighted score (health, latency, quality, trust), apply confidence threshold, budget check
- [x] `src/agent/Mesh/Router/RouterRanking.cs` — `ComputeScore(peer, intent)` — weighted composite: `w_health*health + w_latency*latency + w_quality*quality + w_trust*trust`, normalize to 0-1
- [x] `src/agent/Mesh/Router/RouterHealthTracker.cs` — per-peer rolling health: success rate, recent latency, circuit-open flag; updates from `ITransport.SendAsync` results
- [x] `Config/AppConfig.cs` — add `MeshConfig.MeshRouter` (`MeshRouterOptions`)
- [x] `MeshServiceExtensions.cs` — wire `IMeshRouter`, `RouterHealthTracker`, register in DI
- [x] `MeshController.cs` — add `/api/mesh/router/routes` endpoint: given capability/intent/candidates, return ranked peers
- [x] `tests/Phase4Tests/RouterTests.cs` — unit tests: ranking, health tracking, confidence threshold, budget check, fallback, empty candidates
- [x] `dotnet build` — 0 errors
- [x] `dotnet test Phase3+Phase4` — all tests pass

## Implementation notes

### 2026-08-13

**`src/agent/Mesh/Router/`** — новая папка для router layer:

- `IMeshRouter.cs` — interface: `RouteAsync(capability, ct)` + `RouteAsync(capability, maxCostUsd, ct)` → `Task<IReadOnlyList<PeerCandidate>>`
- `MeshRouterOptions.cs` — config: `Enabled`, `FallbackToPeer`, `MaxPeerCandidates`, `DefaultTimeoutMs`, `MinConfidenceThreshold`, `RouterWeights`
- `PeerCandidate.cs` — record: AgentId, DisplayName, Endpoint, HealthScore, LatencyMs, QualityScore, TrustLevel, CompositeScore, CostHintUsd, CircuitState, TrustPassed, TrustScore
- `RouterRanking.cs` — `ComputeScore(candidate, weights)`: weighted composite score (0-1); `LatencyScore(latencyMs)`; `NormaliseTrust(level)` → trust score mapping
- `RouterHealthTracker.cs` — thread-safe rolling health: success rate, avg latency, circuit-open check; `RecordSuccess(agentId, latencyMs?)`, `RecordFailure(agentId)`, `GetHealthScore(agentId)`, `GetAverageLatencyMs(agentId)`
- `CapabilityMeshRouter.cs` — `IMeshRouter` implementation: capability lookup → trust filter → health enrichment → composite scoring → threshold/budget filter → sort by score

**`Config/AppConfig.cs`** — `MeshConfig.MeshRouter { get; set; } = new()`.

**`Mesh/MeshServiceExtensions.cs`** — DI: `MeshRouterOptions`, `RouterHealthTracker`, `IMeshRouter → CapabilityMeshRouter`.

**`MeshController.cs`** — endpoints: `GET /api/mesh/router/routes?capability=...&maxCostUsd=`, `GET /api/mesh/router/health`.

**`src/hercules-web/`** — `MeshRouterPanel.astro` (capability search, ranked candidates, health overview, circuit breakers), `/mesh` page, nav link.

**`tests/Phase4Tests/RouterTests.cs`** — 33 unit tests.

**Validation:** `dotnet build` — 0 errors; `dotnet test Phase3+Phase4` — 359/359 passed.

## Scope / Likely files
src/agent/Mesh/Router/

## Dependencies
- блокирует / опирается на: [task_022 — semantic-routing](task_022.md)
- блокирует / опирается на: [task_034 — capability-registry](task_034.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Скрытые предпочтения провайдера; явный scoring + observability.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
