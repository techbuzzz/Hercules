# Task 45 — Fan-out / fan-in

**Phase:** 4
**Initiative:** 18
**Status:** done
**Owner:** —
**Slug:** `fan-out-in`

## Goal
Запрос уходит нескольким применимым агентам под строгим concurrency и budget. Ответы schema-validated, выбираются детерминированно, голосованием или опциональным judge.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/Router/FanOutOptions.cs` — config: `MaxConcurrency`, `BudgetCeilingUsd`, `DefaultTimeoutMs`, `Strategy` (Deterministic/Voting/LlmJudge), `MinResponsesForVoting`, `VotingThreshold`, `EnableSchemaValidation`
- [x] `src/agent/Mesh/Router/IFanOutOrchestrator.cs` — interface: `OrchestrateFanOutAsync(envelope, responseSchema, strategy, budget, ct) → AggregationResult`
- [x] `src/agent/Mesh/Router/FanOutOrchestrator.cs` — full implementation: query `IMeshRouter` → parallel fan-out via `ITransport` respecting `MaxConcurrency` + `BudgetCeilingUsd` + per-call `Deadline`; per-peer `TimeoutMs`; schema validation via `ResponseAggregator`
- [x] `src/agent/Mesh/Aggregation/AggregationResult.cs` — result model: `Winner`, `AllResponses`, `SelectionMethod`, `Duration`, `JudgeRationale`, `SchemaViolations`
- [x] `src/agent/Mesh/Aggregation/ResponseAggregator.cs` — aggregation engine: `ValidateSchema(response, schema)`, `AggregateDeterministic(responses, criterion)`, `AggregateVoting(responses, threshold)`, `AggregateWithLlmJudge(envelope, responses)`
- [x] `Config/AppConfig.cs` — add `FanOutOrchestratorOptions FanOut` to `MeshConfig`
- [x] `MeshServiceExtensions.cs` — wire `IFanOutOrchestrator`, `ResponseAggregator` into DI
- [x] `tests/Phase4Tests/FanOutTests.cs` — unit tests: schema validation, deterministic selection (first/highest-conf/latest), voting, LLM-judge fallback, concurrency limiting, budget exceeding, timeout handling, empty responses
- [x] `dotnet build src/agent/Hercules.csproj` — 0 errors
- [x] `dotnet test Phase4` — all Phase 4 tests pass

## Implementation notes

### 2026-08-13

**`src/agent/Mesh/Router/FanOutOptions.cs`** — configuration:
- `FanOutSelectionStrategy` enum: `Deterministic = 0`, `Voting = 1`, `LlmJudge = 2`
- `DeterministicCriterion` enum: `FirstSuccess`, `HighestConfidence`, `Latest`
- `FanOutOptions`: `Enabled`, `MaxConcurrency` (default 5), `BudgetCeilingUsd` (1.00m), `DefaultTimeoutMs` (30s), `Strategy`, `DeterministicCriterion`, `MinResponsesForVoting` (3), `VotingThreshold` (0.51), `EnableSchemaValidation` (true), `EnableLlmJudgeFallback` (true), `MinPeersForFanOut` (2)

**`src/agent/Mesh/Router/IFanOutOrchestrator.cs`** — interface:
- `OrchestrateFanOutAsync(envelope, responseSchema, strategy, budgetUsd, ct) → AggregationResult`

**`src/agent/Mesh/Router/FanOutOrchestrator.cs`** — full implementation:
- Queries `IMeshRouter` for ranked peers filtered by budget
- Single-peer fallback when `peers.Count < MinPeersForFanOut`
- Parallel fan-out via `SemaphoreSlim`-limited concurrency
- Per-peer timeout via linked `CancellationTokenSource`
- Schema validation via `ResponseAggregator`
- Selection via `ResponseAggregator.AggregateAsync()`

**`src/agent/Mesh/Aggregation/AggregationResult.cs`** — result model:
- Factories: `Local()`, `SinglePeer()`, `NoPeers()`
- Fields: `Winner`, `AllResponses`, `ValidResponses`, `SelectionMethod`, `Duration`, `JudgeRationale`, `SchemaViolations`, `PeersContacted`, `SuccessCount`, `HasWinner`

**`src/agent/Mesh/Aggregation/ResponseAggregator.cs`** — aggregation engine:
- `ValidateSchema(response, schema)`: JSON validity check + type/required-property structural validation (no external lib)
- `AggregateDeterministic()`: FirstSuccess / HighestConfidence / Latest by configured `DeterministicCriterion`
- `AggregateVoting()`: text normalization → group by normalized result → majority check vs `VotingThreshold` → fallback to deterministic
- `AggregateWithLlmJudgeAsync()`: builds prompt with all responses → calls `ILLMClient.CompleteAsync()` → parses JSON `best_index` + `rationale` → fallback on exception or parse error

**`Config/AppConfig.cs`** — `MeshConfig.FanOut { get; set; } = new()`.

**`MeshServiceExtensions.cs`** — DI: `FanOutOptions`, `ResponseAggregator`, `IFanOutOrchestrator → FanOutOrchestrator`.

**`tests/Phase4Tests/FanOutTests.cs`** — 29 unit tests:
- FanOutOptions defaults, schema validation (JSON validity, type mismatch, missing required, disabled validation), deterministic selection (highest-confidence, first-success, latest), no-valid-responses, failed-excluded, voting majority, voting no-majority fallback, voting below-min-responses fallback, LLM-judge no-client fallback, AggregationResult factories, orchestrator (disabled, no-peers, single-peer, fan-out, budget, schema violations, strategy override)

**Validation:**
- `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors (19 pre-existing warnings only)
- `dotnet test Phase4` — 131/131 passed
- `dotnet test Phase3` — 297/297 passed

## Scope / Likely files
src/agent/Mesh/Router/FanOut.cs, src/agent/Mesh/Aggregation/

## Dependencies
- блокирует / опирается на: [task_043 — mesh-router](task_043.md)
- блокирует / опирается на: [task_044 — complexity-router](task_044.md)

## Risks / Rollback
Стоимость fan-out; строгие per-request лимиты.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
