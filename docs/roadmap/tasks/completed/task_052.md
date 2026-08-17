# Task 52 — Mesh evaluation suite

**Phase:** 4
**Initiative:** 31
**Status:** done
**Owner:** —
**Slug:** `mesh-eval-suite`

## Goal
Воспроизводимые сценарии измеряют task success, safety denials, routing quality, latency, cost, resilience и деградацию при отказе peer/tool/LLM-провайдера.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Config/AppConfig.cs` — add `MeshEvalConfig MeshEval` to `MeshConfig`
- [x] `src/agent/Config/AppConfig.cs` — `MeshEvalConfig` class with all settings (Enabled, ScenariosDir, ResultsDir, thresholds, chaos config)
- [x] `src/agent/Mesh/Eval/MeshEvalScenario.cs` — models: `MeshEvalScenarioType`, `MeshEvalAssertion`, `MeshEvalScenario`, `MockCapability`, `FailureInjectionConfig`
- [x] `src/agent/Mesh/Eval/MeshEvalResult.cs` — models: `MeshEvalResult`, `MeshEvalMetrics`, `MeshEvalSuiteResult`
- [x] `src/agent/Mesh/Eval/IMeshEvalRunner.cs` — interface: `LoadScenariosAsync`, `RunScenarioAsync`, `RunAllAsync`, `RunByTypeAsync`, `SaveResultAsync`, `LoadBaselineAsync`, `CompareWithBaseline`
- [x] `src/agent/Mesh/Eval/MeshEvalRunner.cs` — implementation: scenario execution engine for all 8 scenario types, dependency resolution, result aggregation
- [x] Scenario templates auto-created in `data/mesh-eval/scenarios/` on first run (task-success, safety-denial, routing-quality, latency, cost, resilience, degradation, chaos)
- [x] `src/agent/Mesh/MeshServiceExtensions.cs` — DI registration for `IMeshEvalRunner`
- [x] `tests/Hercules.Agent.Tests/Phase4Tests/Eval/MeshEvalRunnerTests.cs` — 22 unit tests covering config, loading, execution, assertions, persistence, regression
- [x] `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors
- [x] `dotnet test --filter "FullyQualifiedName~MeshEvalRunner" -c Release` — 22/22 passed

## Implementation notes

### 2026-08-13

**`src/agent/Config/AppConfig.cs`** — `MeshEvalConfig` added to `MeshConfig`:
- `Enabled`, `ScenariosDir`, `ResultsDir`, `MaxScenarioDurationSeconds`
- Per-metric toggles: `EnableTaskSuccessEval`, `EnableSafetyDenialEval`, etc.
- Thresholds: `MinSuccessRateThreshold`, `MaxLatencyThresholdMs`, `MaxCostPerRequestUsd`
- Regression: `BlockOnRegression`, `RegressionThreshold`
- Chaos: `EnableChaosTesting`, `ChaosInjectionRate`

**`src/agent/Mesh/Eval/MeshEvalScenario.cs`** — scenario models:
- `MeshEvalScenarioType` enum: TaskSuccess, SafetyDenial, RoutingQuality, Latency, Cost, Resilience, Degradation, Chaos
- `MeshEvalScenario`: Id, Name, Type, Intent, Assertions, MockPeers, FailureInjection, DependsOn
- `MockCapability`: simulated mesh peer with latency and failure rate
- `FailureInjectionConfig`: timeout, connection_reset, schema_mismatch, peer_unavailable

**`src/agent/Mesh/Eval/MeshEvalResult.cs`** — result models:
- `MeshEvalMetrics`: all metrics (success_rate, latencies, costs, retries, CB opens, chaos)
- `MeshEvalResult`: per-scenario result with assertions
- `MeshEvalSuiteResult`: aggregated suite result with regression tracking

**`src/agent/Mesh/Eval/MeshEvalRunner.cs`** — implementation:
- `LoadScenariosAsync`: loads from JSON files, creates defaults if empty
- `RunScenarioAsync`: executes by type, collects metrics, evaluates assertions
- `RunAllAsync`/`RunByTypeAsync`: suite execution with dependency resolution
- `SaveResultAsync`/`LoadBaselineAsync`: result persistence
- `CompareWithBaseline`: regression detection vs saved baseline
- `EvaluateAssertion`: parses expressions like ">=0.8", "<5000" with invariant culture

**`tests/Hercules.Agent.Tests/Phase4Tests/Eval/MeshEvalRunnerTests.cs`** — 22 unit tests:
- Config defaults, config property setting
- Scenario loading, custom scenarios, invalid file handling
- Per-type scenario execution (TaskSuccess, Latency, Cost, Resilience, Degradation, Chaos)
- Suite execution (RunAllAsync, RunByTypeAsync)
- Result persistence, baseline loading/saving
- Regression comparison
- Assertion evaluation

## Validation

```
dotnet build src/agent/Hercules.csproj -c Release       → 0 errors
dotnet test --filter "FullyQualifiedName~MeshEvalRunner" → 22/22 passed
dotnet test --filter "FullyQualifiedName~Phase4"       → 232/234 passed (2 pre-existing)
```

## Scope / Likely files
src/agent/Mesh/Eval/, tests/Hercules.Agent.Tests/Phase4Tests/Eval/, data/mesh-eval/

## Dependencies
- блокирует / опирается на: [task_042 — protocol-tests](task_042.md) ✓
- блокирует / опирается на: [task_045 — fan-out-in](task_045.md) ✓
- блокирует / опирается на: [task_047 — retry-timeout-breaker](task_047.md) ✓

## Risks / Rollback
Реалистичные сценарии дороги; shared scenario library.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
