# Task 48 — Границы делегации

**Phase:** 4
**Initiative:** 23
**Status:** done
**Owner:** —
**Slug:** `delegation-boundaries`

## Goal
Mesh ограничивает hop count, fan-out width, кумулятивные tool calls, общую стоимость и время на запрос. Агенты могут отклонить делегацию, чтобы не превышать свою policy или capacity.

## Acceptance criteria
- [x] `DelegationBoundaryConfig` in `MeshConfig` (max hop count, fan-out width, cumulative tool calls, cost, time)
- [x] `IDelegationBoundaryService` interface + `DelegationBoundaryService` implementation
- [x] `DelegationBoundaryContext` model (hop count, tool calls, cost, wall-clock)
- [x] Outgoing delegation check: reject before fan-out if boundaries exceeded
- [x] Incoming delegation check: decline or soft-warn on boundary violation
- [x] FanOutOrchestrator integration (pre-check before routing)
- [x] DI registration in `MeshServiceExtensions`
- [x] `appsettings.json` updated with `DelegationBoundaries` section
- [x] Unit tests for all boundary checks (hop, width, tool calls, cost, time)
- [x] `dotnet build` + `dotnet test` pass

## Sub-tasks
- [x] Add `DelegationBoundaryConfig` to `MeshConfig` in `AppConfig.cs`
- [x] Create `DelegationBoundaryContext` model
- [x] Create `IDelegationBoundaryService` interface
- [x] Implement `DelegationBoundaryService` (hop, width, tool calls, cost, time checks)
- [x] Integrate boundary checks into `FanOutOrchestrator` (pre-check before routing)
- [x] Integrate boundary checks into incoming delegation path (decline with reason)
- [x] DI registration in `MeshServiceExtensions`
- [x] Add `DelegationBoundaries` section to `appsettings.json`
- [x] Write unit tests in `tests/.../Mesh/Budget/DelegationBoundaryTests.cs`
- [x] `dotnet build` + `dotnet test` pass

## Validation
- `dotnet build src/agent/Hercules.csproj` — succeed
- `dotnet test --filter "FullyQualifiedName~DelegationBoundary"` — pass

## Implementation notes
Hop count enforcement uses existing `AuthContext.DelegationDepth` field. Fan-out width checked against `DelegationBoundaryConfig.MaxFanOutWidth` before `FanOutOrchestrator.OrchestrateFanOutAsync`. Cumulative tool calls and cost tracked per request chain via in-memory `ConcurrentDictionary<string, DelegationBoundaryContext>` keyed by root request ID.

## Scope / Likely files
src/agent/Mesh/Budget/

## Dependencies
- блокирует / опирается на: [task_012 — budget-guardrails](task_012.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Слишком жёсткие границы блокируют легитимные задачи; явный reason code + метрики.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
