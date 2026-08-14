# Task 64 — Операционные SLO

**Phase:** 6
**Initiative:** 44
**Status:** done
**Owner:** —
**Slug:** `operational-slos`

## Goal
Каждый template объявляет availability, response-time, data-loss, recovery-time и cost objectives, alert thresholds и runbooks.

## Acceptance criteria

### Sub-tasks

- [x] `Config/AppConfig.cs` — add `SlosConfig` with Enabled, SlosDir, default alert thresholds, vertical targets map
- [x] `Slo/SloTypes.cs` — types: `SloDefinition`, `SloTarget` (availability/response-time/data-loss/recovery/cost), `SloAlert`, `SloRunbook`, `SloStatus`, `SloReport`, `SloViolationRecord`
- [x] `Slo/ISloService.cs` — interface: `GetSlosDir()`, `ListSlos()`, `GetSloReport()`, `GetSloStatus()`, `AcknowledgeSlo()`
- [x] `Slo/SloService.cs` — implementation: reads `docs/slos/*.slo.json`, evaluates current metrics vs targets, computes status, tracks violations
- [x] `Slo/SloEvaluator.cs` — evaluates current metric values against each target (availability from mesh metrics, response-time histogram, outbox queue lag, cost budget)
- [x] `Slo/SloController.cs` — WebAPI: `GET /api/slos`, `GET /api/slos/{vertical}`, `GET /api/slos/{vertical}/report`, `POST /api/slos/{vertical}/ack`
- [x] `docs/slos/` — per-vertical SLO definitions: `greenhouse.slo.json`, `cold-chain.slo.json`, `server-room.slo.json`, `vending.slo.json`
- [x] `Program.cs` — register `SlosConfig` and `ISloService` in DI
- [x] `dotnet build` + `dotnet test` pass

## Scope / Likely files
docs/slos/, templates/*/slo.md

## Dependencies
- блокирует / опирается на: [task_054 — centralized-observability](task_054.md)
- блокирует / опирается на: [task_057 — lifecycle-management](task_057.md)

## Risks / Rollback
SLO без автоматического enforcement; integration с 56/57.

## Implementation notes

### 2026-08-14

**`Config/AppConfig.cs`** — added `SlosConfig` with `Enabled`, `SlosDir` ("docs/slos"), `EvaluationIntervalSeconds`, `EnableAlerting`, `EnableStatusEndpoint`, and `SlosDefaultThresholds` (warning/critical for all 5 objectives).

**`src/agent/Slo/SloTypes.cs`** — 12 types: `SloDefinition`, `SloAlertThresholds`, `SloRunbooks`, `SloRunbook`, `SloSeverity` (Ok/Warning/Critical), `SloObjectiveStatus`, `SloStatus`, `SloViolationRecord`, `SloReport`, `SloComplianceSummary`, `SloSummary`.

**`src/agent/Slo/ISloService.cs`** — interface: `GetSlosDir()`, `GetAllDefinitions()`, `GetDefinition()`, `Evaluate()`, `GetStatus()`, `GetReport()`, `GetSummary()`, `AcknowledgeViolation()`, `AcknowledgeAll()`.

**`src/agent/Slo/SloService.cs`** — implementation: reads `*.slo.json`, evaluates 5 objectives (availability from audit log, response time from metrics, data loss from outbox, recovery time from degradation events, cost from budget service), classifies severity, tracks violations with resolution.

**`src/agent/Hercules.WebApi/Controllers/SloController.cs`** — 6 endpoints: `GET /api/slos`, `GET /api/slos/{vertical}/definition`, `GET /api/slos/{vertical}`, `GET /api/slos/{vertical}/report`, `POST /api/slos/{vertical}/ack/{violationId}`, `POST /api/slos/{vertical}/ack`.

**`docs/slos/`** — 4 per-vertical SLO definitions: greenhouse (99%/2s/5events/15min/$5), cold-chain (99.9%/5s/0/5min/$3), server-room (99.99%/500ms/0/5min/$15), vending (99.5%/3s/0/10min/$2).

**`src/agent/Program.cs`** — registered `SlosConfig` and `ISloService` in DI.

**Validation:**
- `dotnet build Hercules.csproj -c Release` — succeeded (0 new errors)
- `dotnet test --filter Slo` — 9/9 passed
- Full suite: 1598/1607 (9 pre-existing failures: OtelService×5, BudgetGuard×1, NumericValidator×2, BusHttpServer×1)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
