# Task 40 — Trust и admission policy

**Phase:** 3
**Status:** done
**Owner:** —
**Slug:** `trust-admission`

## Goal
Peer-агенты allow-listed по identity и capability. Вызовы отклоняются, когда intent, data classification, schema version, budget или risk level не разрешены.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/Policy/TrustAdmissionPolicy.cs` — types: `TrustLevel` enum (Unverified/ProvisionallyTrusted/Trusted/Verified), `DataClassification` enum (Public/Internal/Confidential/Restricted), `SchemaVersionMismatch`, `BudgetLimitExceeded`, `IntentMismatch`, `RiskLevelMismatch` — decision reasons
- [x] `src/agent/Mesh/Policy/TrustAdmissionContext.cs` — context record: identity (IdentityResult), envelope (IntentEnvelope), targetAgentId, targetCapabilities, targetResourceLimits, requestSchemaVersion
- [x] `src/agent/Mesh/Policy/TrustAdmissionResult.cs` — result record: `Allowed`, `Denied(reason)`, `DryRun` factory methods; `IsAllowed`, `DenialReason`
- [x] `src/agent/Mesh/Policy/ITrustAdmissionPolicy.cs` — interface: `Evaluate(TrustAdmissionContext)`, `PolicyMode` (Enforce/DryRun/Disabled), `AllowedTrustLevels`, `AllowedIntents`, `AllowedClassifications`, `AllowSchemaMismatch`, `AllowBudgetExceeded`
- [x] `src/agent/Mesh/Policy/TrustAdmissionPolicyEngine.cs` — implementation: dry-run → trust level → intent allow-list → data classification → schema version → budget/resource limits → risk level check
- [x] `Config/AppConfig.cs` — `MeshConfig.TrustAdmission` (`TrustAdmissionConfig`): `Enabled`, `PolicyMode`, `AllowedTrustLevels`, `AllowedIntents`, `AllowSchemaMismatch`, `AllowBudgetExceeded`, `AllowedClassifications`, `AllowedRiskLevels`
- [x] `MeshServiceExtensions.cs` — wire `ITrustAdmissionPolicy`, `TrustAdmissionPolicyEngine` into mesh DI
- [x] `MeshController.cs` — endpoints: `GET /api/mesh/policy/status` (policy config + mode), `POST /api/mesh/policy/dry-run` (evaluate without enforcing); integration into `/api/mesh/intent` endpoint
- [x] `tests/Phase3Tests/TrustAdmissionPolicyTests.cs` — 27 unit tests: trust level checks, intent allow/deny, classification filtering, schema mismatch, budget limit, dry-run mode, disabled mode, policy context building, mode parsing
- [x] `dotnet build` — 0 errors
- [x] `dotnet test Phase3` — all Phase 3 tests pass (212/212)

## Implementation notes

### 2026-08-13

**`src/agent/Mesh/Policy/`** — новый namespace для trust admission policy:

- `TrustAdmissionPolicy.cs` — `TrustLevel` enum (Unverified/ProvisionallyTrusted/Trusted/Verified), `DataClassification` enum (Public/Internal/Confidential/Restricted), `TrustDenialReason` enum (TrustLevelTooLow/IntentNotAllowed/ClassificationTooHigh/SchemaVersionMismatch/BudgetLimitExceeded/RiskLevelMismatch/IdentityNotAllowListed)
- `TrustAdmissionContext.cs` — полный контекст запроса: CallerIdentity, Envelope, TargetAgentId, TargetCapabilities, TargetResourceLimits, TargetMinSchemaVersion, CallerTrustLevel, DataClassification, RequestedRiskLevel, CallerSchemaVersion
- `TrustAdmissionResult.cs` — result record: Allowed/Denied factory methods, IsAllowed, DenialReason, DenialCode, DryRun
- `ITrustAdmissionPolicy.cs` — интерфейс: Evaluate(ctx), PolicyMode property; `PolicyMode` enum: Enforce/DryRun/Disabled
- `TrustAdmissionPolicyEngine.cs` — реализация: парсит PolicyMode из строки, Evaluates в порядке disabled→dry-run→trust→intent→classification→schema→budget→risk; IsLevelAllowed использует enum comparison через парсинг

**`Config/AppConfig.cs`** — `TrustAdmissionConfig` class + `MeshConfig.TrustAdmission` property:
- Enabled (default true), PolicyMode (default DryRun), AllowedTrustLevels, AllowedIntents, AllowedClassifications, AllowSchemaMismatch (default true), AllowBudgetExceeded (default true), AllowedRiskLevels

**`Mesh/MeshServiceExtensions.cs`** — добавлена регистрация `TrustAdmissionConfig` + `ITrustAdmissionPolicy` → `TrustAdmissionPolicyEngine`

**`MeshController.cs`** — интеграция и endpoints:
- `POST /api/mesh/intent` — перед routing вызывает policy.Evaluate(), при Denied (не DryRun) возвращает 403
- `GET /api/mesh/policy/status` — текущая конфигурация и effective mode
- `POST /api/mesh/policy/dry-run` — evaluate без enforcement, принимает TrustAdmissionDryRunRequest
- Фикс: добавлен `using Hercules.Mesh.Auth` (GetMeshIdentity extension), `using Hercules.Config` (TrustAdmissionConfig), паттерн для `IsCacheFresh` на IDiscoveryService
- Helper: `ParseTrustLevel()`, `ParseDataClassification()` для endpoint

**`tests/.../Phase3Tests/TrustAdmissionPolicyTests.cs`** — 27 unit тестов:
- Disabled/Enforce/DryRun modes, trust level parsing и comparison, intent allow-list, data classification, schema version compatibility (major.minor, fallback на string compare), risk level, budget (payload length → token estimate), TrustAdmissionResult factory, PolicyMode parsing (case-insensitive, default DryRun)

**Validation:**
- `dotnet build Hercules.csproj -c Release` — 0 errors (15 pre-existing warnings)
- `dotnet build Hercules.WebApi.csproj -c Release` — 0 errors (13 pre-existing warnings)
- `dotnet test --filter Phase3` — 212/212 passed
- `dotnet test --filter TrustAdmissionPolicy` — 27/27 passed
- `dotnet test --filter Phase4` — 29/29 passed

## Scope / Likely files
src/agent/Mesh/Policy/

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)
- блокирует / опирается на: [task_039 — identity-delegation](task_039.md)

## Risks / Rollback
Слишком строго => false negatives; нужны dry-run отчёты.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
