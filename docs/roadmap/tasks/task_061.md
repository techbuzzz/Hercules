# Task 61 — Local-first degradation

**Phase:** 6
**Initiative:** 42
**Status:** done
**Owner:** —
**Slug:** `local-degradation`

## Goal
Когда cloud LLM, peers или сеть недоступны, агент следует configured safe fallback: deterministic rules, local skills, reduced-capability models, queued work, operator notification.

## Acceptance criteria
- [x] Define degradation modes enum (Offline, Degraded, Full)
- [x] Create DegradationManager with health-check orchestration
- [x] Implement DeterministicFallbackEngine for rule-based decisions
- [x] Implement SafeFallbackStrategies: local skills, reduced-capability models, queued work
- [x] Add operator notification system (webhook/email/telegram)
- [x] Add degradation state observability (metrics, logs, status endpoint)
- [x] Create degradation configuration schema (appsettings.json)
- [x] Add unit tests for fallback logic
- [x] Document degradation behavior in README

## Scope / Likely files
src/agent/Degradation/

## Dependencies
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)
- блокирует / опирается на: [task_022 — semantic-routing](task_022.md)
- блокирует / опирается на: [task_060 — offline-resilience](task_060.md)

## Implementation notes
- Created `src/agent/Degradation/` directory with:
  - `DegradationMode.cs` - enum and record types for mode/health/status
  - `DegradationConfig.cs` - configuration for health checks, fallback, notifications, observability
  - `DegradationManager.cs` - BackgroundService orchestrating health checks and mode transitions
  - `DeterministicFallbackEngine.cs` - rule-based fallback strategy selection
  - `OperatorNotificationService.cs` - webhook/Telegram/email notifications
  - `DegradationObservability.cs` - metrics, logging, status reports
- Updated `AppConfig.cs` with `DegradationConfig` property
- Updated `Program.cs` with DI registration
- Added unit tests in `tests/Hercules.Agent.Tests/Degradation/DegradationTests.cs` (19 tests passing)

## Validation
- Build: `dotnet build src/agent/Hercules.csproj` - succeeded
- Tests: `dotnet test --filter DegradationTests` - 19 passed

## Risks / Rollback
Неожиданная silent degradation; явный mode flag + observability.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
