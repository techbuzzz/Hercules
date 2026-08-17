# Task 56 — Rate limits и квоты

**Phase:** 6
**Initiative:** 35
**Status:** done
**Owner:** —
**Slug:** `rate-limits-quotas`

## Goal
Per-agent, per-skill, per-user, per-tenant лимиты concurrency, calls, токенов, оценочной стоимости, storage, message volume.

## Acceptance criteria
- [x] Add `QuotasConfig` section to `AppConfig.cs` (per-agent, per-skill, per-user, per-tenant limits)
- [x] Create `src/agent/Quotas/Models.cs` — QuotaLimitType enum, QuotaScope enum, QuotaStatus record, QuotaViolation record
- [x] Create `src/agent/Quotas/IQuotaService.cs` interface with CheckLimit, RecordUsage, GetStatus, Reset methods
- [x] Implement `QuotaService.cs` — in-memory quota tracking with sliding window rate limiting
- [x] Create `src/agent/Quotas/QuotaGuard.cs` — enforcement helper with graceful degradation
- [x] Integrate quota checks into `AgentCore` (pre-request validation)
- [x] Add quota tracking to skill execution path (concurrency tracking)
- [x] Add quota tracking to mesh delegation path
- [x] Implement rate limit headers (RateLimitInfo for HTTP response headers)
- [x] Add `QuotasController` — GET /api/quotas, GET /api/quotas/{scope}/{id}, GET /api/quotas/rate-limit
- [x] Register quota services in DI (CLI and WebAPI Program.cs)
- [x] Unit tests for quota enforcement logic (27 tests: QuotaServiceTests + QuotaGuardTests)
- [x] `dotnet build` + `dotnet test` pass (1496 tests pass, 8 pre-existing failures)

## Scope / Likely files
src/agent/Mesh/Quotas/

## Dependencies
- блокирует / опирается на: [task_012 — budget-guardrails](task_012.md)
- блокирует / опирается на: [task_048 — delegation-boundaries](task_048.md)

## Risks / Rollback
Многоуровневые лимиты сложно отлаживать; явный report при отказе.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)

## Implementation notes
- Created `src/agent/Quotas/` with 4 files (Models.cs, IQuotaService.cs, QuotaService.cs, QuotaGuard.cs)
- `QuotaService`: In-memory quota tracking with ConcurrentDictionary, sliding window rate limiting via ConcurrentBag<DateTime>
- `QuotaGuard`: Enforcement helper with CheckAndGetDegradationMessage and LogSoftWarnings
- `QuotaLimitType` enum: 12 limit types covering agent, skill, user, and tenant scopes
- `QuotaScope` enum: Agent, Skill, User, Tenant
- AgentCore integration: pre-check on HandleAsync, Begin/End concurrency tracking, usage recording after LLM calls
- `QuotasController`: 4 API endpoints for quota status and rate limit info
- DI registration: CLI and WebAPI Program.cs
- Tests: 27 unit tests (QuotaServiceTests + QuotaGuardTests)
- Build: succeeded | Tests: 1496 passed, 8 pre-existing failures (NumericValidatorTests)
