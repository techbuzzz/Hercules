# Task 44 — Сложность и стоимость

**Phase:** 4
**Initiative:** 17
**Status:** done
**Owner:** —
**Slug:** `complexity-router`

## Goal
Дешёвое детерминированное правило или маленький классификатор выбирает: direct skill / small model / large model / one peer / fan-out. Решения измеримы и конфигурируемы.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/Router/ComplexityLevel.cs` — `ComplexityLevel` enum: `Simple`, `Moderate`, `Complex`
- [x] `src/agent/Mesh/Router/IComplexityClassifier.cs` — `IComplexityClassifier` interface: `Classify(intent, payloadSizeBytes, toolCount, estimatedCostUsd, ct) → ComplexityLevel`
- [x] `src/agent/Mesh/Router/ComplexityClassifier.cs` — rule-based classifier: keyword heuristics + configurable thresholds; no ML dependency
- [x] `src/agent/Mesh/Router/ComplexityIntentAnalysis.cs` — analysis model: `PayloadSizeBytes`, `ToolCount`, `EstimatedCostUsd`, `IntentKeywords`, `HasSafetySensitiveTool`, `EstimatedComplexityScore`
- [x] `src/agent/Mesh/Router/ComplexityRoutingDecision.cs` — decision record: `ComplexityLevel`, `ExecutionPath`, `Reason`, `EstimatedCostUsd`, `Confidence`, `SuggestedMaxRetries`
- [x] `src/agent/Mesh/Router/ComplexityRoutingDecision+ExecutionPath.cs` — enum: `LocalDirect`, `LocalSmallModel`, `LocalLargeModel`, `SinglePeer`, `FanOut`, `Blocked`
- [x] `src/agent/Mesh/Router/ComplexityRouter.cs` — `IComplexityRouter`: `ClassifyAndRouteAsync(intent, payloadSize, toolCount, costBudgetUsd, availablePeers, ct) → ComplexityRoutingDecision`. Integrates with `IComplexityClassifier` and `IMeshRouter`
- [x] `src/agent/Mesh/Router/ComplexityRouterOptions.cs` — config: `SimpleMaxPayloadBytes`, `SimpleMaxCostUsd`, `ModerateMaxPayloadBytes`, `ModerateMaxCostUsd`, `MaxPeersForFanOut`, `EnableFanOut`, `SmallModelCostThresholdUsd`, `LargeModelCostThresholdUsd`
- [x] `Config/AppConfig.cs` — add `ComplexityRouterOptions ComplexityRouter` to `MeshConfig`
- [x] `MeshServiceExtensions.cs` — wire `IComplexityClassifier`, `IComplexityRouter` via DI
- [x] `tests/Phase4Tests/ComplexityRouterTests.cs` — unit tests: classifier rules, decision logic, path selection, threshold boundaries, peer count limits
- [x] `dotnet build src/agent/Hercules.csproj` — 0 errors
- [x] `dotnet test Phase4` — all Phase 4 tests pass

## Implementation notes

### 2026-08-13

**`src/agent/Mesh/Router/ComplexityLevel.cs`** — `ComplexityLevel` enum: `Simple = 0`, `Moderate = 1`, `Complex = 2`.

**`src/agent/Mesh/Router/ComplexityRoutingDecision.cs`** — `ExecutionPath` enum (6 values: `LocalDirect` through `Blocked`) and `ComplexityRoutingDecision` record with all decision metadata.

**`src/agent/Mesh/Router/ComplexityIntentAnalysis.cs`** — analysis model with keywords, safety flags, complexity score.

**`src/agent/Mesh/Router/ComplexityClassifier.cs`** — rule-based classifier:
- Tokenizes intent text, extracts keywords via substring matching
- Computes weighted score: payload (0.30) + tool count (0.25) + cost (0.15) + keywords (0.30)
- Thresholds: Simple < 0.20, Moderate < 0.50, Complex ≥ 0.50
- No ML dependency; keyword heuristics only

**`src/agent/Mesh/Router/ComplexityRouter.cs`** — `IComplexityRouter` + `ComplexityRouter` implementation:
- Pipeline: analyze → classify → budget check → determine path → query mesh router for peers
- Path selection rules:
  - `requestsPeer=true` → FanOut (if EnableFanOut) else SinglePeer
  - `noLocalCapability=true` → FanOut (if safety-sensitive + EnableFanOut) else SinglePeer
  - Simple + no tools → LocalDirect; Simple + tool → LocalSmallModel (if cost > threshold)
  - Moderate → LocalSmallModel or LocalLargeModel (cost threshold)
  - Complex → LocalLargeModel or FanOut (if safety-sensitive + EnableFanOut)
  - Budget exceeded → Blocked
  - No peers available → downgrades to LocalLargeModel

**`src/agent/Mesh/Router/ComplexityRouterOptions.cs`** — full config with cost thresholds, fan-out limits, retry counts.

**`Config/AppConfig.cs`** — `MeshConfig.ComplexityRouter { get; set; } = new()`.

**`MeshServiceExtensions.cs`** — DI: `IComplexityClassifier → ComplexityClassifier`, `IComplexityRouter → ComplexityRouter`.

**`tests/Phase4Tests/ComplexityRouterTests.cs`** — 23 unit tests covering: classifier scoring, threshold mapping, fast classification, complexity routing decisions, peer routing with registered peers, fan-out limits, disabled router, blocked budget, retry counts.

**Validation:**
- `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors (pre-existing warnings only)
- `dotnet test Phase4` — 102/102 passed
- `dotnet test Phase3+Phase4` — 399/399 passed

## Scope / Likely files
src/agent/Mesh/Router/ComplexityRouter.cs

## Dependencies
- блокирует / опирается на: [task_043 — mesh-router](task_043.md)

## Risks / Rollback
Сложность ML-классификатора; версия на rules + опциональный ML.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
