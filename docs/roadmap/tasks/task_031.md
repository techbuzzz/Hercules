# Task 31 — Симуляция шаблонов

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `template-simulation`

## Goal
Шаблоны включают replayable sensor/event fixtures и failure scenarios; валидация без реального железа или внешних side-effects.

## Acceptance criteria

### Sub-tasks

- [x] `Simulation/Models.cs` — types: `SensorReading`, `EventFixture`, `FailureScenario` (with `FailureType` enum: StuckAt/DataLoss/Spike/Noise/Latency/Blackout), `SimulationSession`, `SimulationResult`, `SimulationMetrics`, `AppliedFailure`, `ReplayMode`
- [x] `Simulation/ISensorSimulator.cs` — interface: `HasFixtures`, `LoadSensorFixtures`, `LoadEventFixtures`, `LoadFailureScenarios`, `GetSimPath`
- [x] `Simulation/FileSensorSimulator.cs` — reads from `templates/{scenario}/sim/` (sensors.json, events.json, failures.json); graceful empty returns on missing files
- [x] `Simulation/FailureScenarioEngine.cs` — `InjectFailures` (applies failure types to readings within injection window), `GenerateFailureEvents` (creates failure_injected/failure_resolved events), `GetFailureCoverage` (sensor→scenarios mapping); DurationSeconds=0 means no upper boundary (always-on from injection point)
- [x] `Simulation/TemplateSimulationService.cs` — orchestrator: `StartSession`, `RunReplay` (with/without failure injection), `StopSession`, `GetSession`, `GetSimulatableTemplates`, `HasFixtures`, `GetFailureCoverage`; tracks active sessions in `ConcurrentDictionary`
- [x] `SimulationController.cs` — WebAPI endpoints: `GET /api/simulation/templates`, `GET /api/simulation/templates/{name}/available`, `POST /api/simulation/sessions`, `GET /api/simulation/sessions/{name}`, `POST /api/simulation/sessions/{name}/replay`, `DELETE /api/simulation/sessions/{name}`; request/response DTOs
- [x] DI registration (CLI + WebAPI Program.cs): `ISensorSimulator` → `FileSensorSimulator`, `FailureScenarioEngine`, `TemplateSimulationService`
- [x] 4 template fixture bundles: `templates/{greenhouse,cold-chain,server-room,vending}/sim/` — sensors.json, events.json, failures.json
- [x] `tests/.../Simulation/FailureScenarioEngineTests.cs` — 18 tests: all failure types (StuckAt, DataLoss, Spike, Noise, Latency, Blackout), boundary conditions, Duration=0 (always-on), case-insensitive sensors, unparseable timestamps, coverage mapping, event generation
- [x] `tests/.../Simulation/TemplateSimulationServiceTests.cs` — 10 tests: session lifecycle, replay with/without failures, failure events, error cases (no session), coverage, empty templates
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (28 simulation tests pass; 7 pre-existing failures unrelated to this task)

## Scope / Likely files
templates/*/sim/, src/agent/Simulation/, src/agent/Hercules.WebApi/Controllers/SimulationController.cs

## Dependencies
- блокирует / опирается на: [task_030 — agent-templates](task_030.md)

## Risks / Rollback
Сложность моделирования неисправностей; reuse реальных eval-фикстур.

## Implementation notes

### 2026-08-13

**Добавлено:**

**`src/agent/Simulation/`** — 5 файлов:
- `Models.cs` — все типы: `SensorReading`, `EventFixture`, `FailureScenario` (+ `FailureType` enum), `SimulationSession`, `SimulationResult`, `SimulationMetrics`, `AppliedFailure`, `ReplayMode`
- `ISensorSimulator.cs` — интерфейс для загрузки fixtures (читабельный, тестируемый)
- `FileSensorSimulator.cs` — загружает fixtures из `templates/{scenario}/sim/`; graceful empty на отсутствующие файлы
- `FailureScenarioEngine.cs` — ядро failure injection:
  - `InjectFailures`: applies each failure type (StuckAt/DataLoss/Spike/Noise/Latency/Blackout) to readings within the injection window
  - `GenerateFailureEvents`: creates `failure_injected` / `failure_resolved` events from scenarios
  - `GetFailureCoverage`: maps sensors → failure scenarios
  - DurationSeconds=0 → no upper boundary (always-on from injection point forward)
  - `DateTimeStyles.AssumeUniversal | AdjustToUniversal` для корректного парсинга "Z"-suffix и "+00:00" timestamps
- `TemplateSimulationService.cs` — DI-friendly orchestrator; управляет активными сессиями в `ConcurrentDictionary`

**`Hercules.WebApi/Controllers/SimulationController.cs`** — 6 endpoints:
- `GET /api/simulation/templates` — список шаблонов с fixtures
- `GET /api/simulation/templates/{name}/available` — проверка наличия fixtures
- `POST /api/simulation/sessions` — начать симуляционную сессию
- `GET /api/simulation/sessions/{name}` — состояние сессии
- `POST /api/simulation/sessions/{name}/replay` — запуск replay (опционально с failure injection)
- `DELETE /api/simulation/sessions/{name}` — остановить сессию
- Все request/response DTOs: `SimulatableTemplateDto`, `TemplateAvailabilityDto`, `StartSessionRequest`, `ReplayRequest`, `SimulationSessionDto`, `SimulationResultDto`, `AppliedFailureDto`, `SimulationMetricsDto`

**DI (CLI + WebAPI):**
- `ISensorSimulator` → `FileSensorSimulator` (TemplatesBaseDir = `{AppContext.BaseDirectory}/templates`)
- `FailureScenarioEngine` singleton
- `TemplateSimulationService` singleton

**Templates fixtures** (`templates/{greenhouse,cold-chain,server-room,vending}/sim/`):
- `sensors.json` — SensorReading[] с timestamp (ISO 8601 "Z"-suffix), sensor name, value, unit, location, status
- `events.json` — EventFixture[] с timestamp, type, description, severity, payload, source
- `failures.json` — FailureScenario[] с id, name, description, type (enum string), affectedSensors, injectAfterSeconds, durationSeconds, faultValue, faultMessage

**Bug fix — FailureScenarioEngine boundary condition:**
- Old: `if (elapsed > InjectAfterSeconds + DurationSeconds)` — DurationSeconds=0 → always `true` → ALL readings skipped
- New: `if (DurationSeconds > 0 && elapsed > InjectAfterSeconds + DurationSeconds)` — DurationSeconds=0 → no upper boundary → always-on from injection point

**Bug fix — Timestamp parsing:**
- Added `DateTimeStyles.AssumeUniversal | AdjustToUniversal` to `DateTimeOffset.TryParse` to correctly handle "Z"-suffix and "+00:00" roundtrip-format timestamps in all system timezones

**Tests** — 28 новых тестов:
- `FailureScenarioEngineTests.cs` — 18 тестов: StuckAt, DataLoss, Spike, Noise, Latency, Blackout, boundary (t=3100 at window end), Duration=0 (always-on), case-insensitive, unparseable timestamp, coverage, events
- `TemplateSimulationServiceTests.cs` — 10 тестов: session lifecycle, replay with/without failures, failure events, no-session error, coverage, empty templates

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet test --filter Simulation` — 28/28 passed
- `dotnet test` — 866/873 passed (7 pre-existing: OtelService × 5, BudgetGuard × 1, WasmTool × 1 — unrelated to task_031)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
