# Task 58 — Configuration и policy rollout

**Phase:** 5
**Initiative:** 41
**Status:** done
**Owner:** —
**Slug:** `config-policy-rollout`

## Goal
Подписанные версионированные configuration и policy бандлы: staged rollout, local validation, expiry, rollback, last-known-good fallback.

## Acceptance criteria
- [x] Add `ConfigRolloutConfig` section to `AppConfig.cs` (bundles path, signing required, staging groups, validation timeout)
- [x] Create `src/agent/Config/Rollout/Models.cs` — `ConfigBundle`, `BundleStage`, `BundleStatus`, `RolloutState` records
- [x] Create `src/agent/Config/Rollout/ISignedBundleValidator.cs` interface with `ValidateAsync` method
- [x] Create `src/agent/Config/Rollout/SignedBundleValidator.cs` — HMAC-SHA256 signature verification (reuses `PackageSigningService` approach)
- [x] Create `src/agent/Config/Rollout/IRolloutManager.cs` interface with `ApplyBundleAsync`, `PromoteStageAsync`, `RollbackAsync`, `GetCurrentState` methods
- [x] Create `src/agent/Config/Rollout/RolloutManager.cs` — staged rollout logic with expiry checks, last-known-good fallback
- [x] Create `src/agent/Config/Rollout/LocalConfigValidator.cs` — schema/constraint validation before applying a bundle
- [x] Integrate `RolloutManager` into `RuntimeConfigStore` — new config bundles flow through rollout instead of direct `Update`
- [x] Add `RolloutController` — GET /api/rollout/state, POST /api/rollout/apply, POST /api/rollout/promote, POST /api/rollout/rollback
- [x] Register rollout services in DI (WebAPI Program.cs)
- [x] Unit tests for `RolloutManager` (stage promotion, expiry, rollback, last-known-good)
- [x] `dotnet build` + `dotnet test` pass (existing tests unaffected)

## Scope / Likely files
src/agent/Config/Rollout/

## Dependencies
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)
- блокирует / опирается на: [task_055 — security-ops](task_055.md)

## Risks / Rollback
Bad config rollout; staged + dry-run + monitoring.

## Implementation notes
- Created `src/agent/Config/Rollout/` with 7 files: Models.cs, ISignedBundleValidator.cs, SignedBundleValidator.cs, IRolloutManager.cs, RolloutManager.cs, LocalConfigValidator.cs, RolloutExpiryChecker.cs
- `ConfigRolloutConfig`: bundles path, signature requirements, staging duration, expiry checker interval, LKG settings
- `SignedBundleValidator`: HMAC-SHA256 signature validation, trusted signers by fingerprint, version compatibility checks
- `RolloutManager`: Pending→Staging→Production→Retired lifecycle; auto-promotes to Staging on apply; saves LKG before promoting to Production; applies config to `RuntimeConfigStore` on promotion; full rollback to LKG with reason tracking
- `LocalConfigValidator`: JSON parse validation, payload size limits, config/policy type-specific checks, semver version warnings
- `RolloutExpiryChecker`: BackgroundService that retires expired bundles periodically
- `RolloutController`: 5 REST endpoints (state, apply, promote, rollback, bundle/{id})
- DI registration in WebAPI `Program.cs`
- Tests: 15 new tests (RolloutManagerTests + LocalConfigValidatorTests) — all pass
- Build: succeeded | Tests: 1534 pass, 8 pre-existing failures (OtelService, NumericValidator, BudgetGuard)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
