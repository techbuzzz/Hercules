# Task 20 — Манифест навыка и совместимость

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `skill-manifest`

## Goal
skill.meta.json: ID, semver, owner, поддерживаемые версии Hercules, версии input/output схем, required tools, permissions, model requirements, risk level, бюджеты.

## Acceptance criteria

### Sub-tasks

- [x] `SkillManifest.cs` (`src/agent/Skills/Package/`) — manifest record с полями:
  - `SchemaVersion` (semver string, default "1.0.0")
  - `Owner` (string, author/maintainer)
  - `MinHerculesVersion` / `MaxHerculesVersion` (semver, диапазон совместимых версий Hercules)
  - `InputSchemaVersion` / `OutputSchemaVersion` (semver, версии схем ввода/вывода навыка)
  - `RequiredTools` (List<string>, список имён необходимых инструментов)
  - `Permissions` (List<string>, запрошенные permissions: Read, Write, Network, Memory, etc.)
  - `ModelRequirements` (string, минимальные требования к модели: context window, capabilities)
  - `RiskLevel` (enum: Low=0, Medium=1, High=2, Critical=3)
  - `Budget` (manifest budgets: MaxTokensPerCall, MaxCallsPerMinute, MaxCostPerCall)
- [x] `SkillManifestValidator.cs` — валидация: `Validate(SkillManifest)` → список ошибок; проверяет schema drift по SchemaVersion, диапазон Hercules-версий, RequiredTools против зарегистрированных
- [x] `SkillMeta` (Storage/Models.cs) — добавить поля: `Owner`, `MinHerculesVersion`, `MaxHerculesVersion`, `InputSchemaVersion`, `OutputSchemaVersion`, `Permissions`, `ModelRequirements`, `RiskLevel`, `Budget`
- [x] `SkillPackageManifest` + `SkillPackageSkillMeta` (`Skills/SkillPackageManifest.cs`) — расширить новыми полями из manifest
- [x] `SkillPackager.cs` — при Export: включать все manifest-поля в skill.meta.json и skill.package.json
- [x] `FileSkillRepository.cs` — при Save/Load: round-trip всех manifest-полей SkillMeta (JSON serialization, PropertyNameCaseInsensitive)
- [x] `SkillManager` — manifest-совместимость проверяется через SkillManifestValidator на уровне API (Controller → Validator)
- [x] `SkillManifestController.cs` (WebAPI) — `GET /api/skills/{id}/manifest` — получить манифест; `POST /api/skills/{id}/manifest/validate` — валидировать совместимость; `POST /api/skills/manifest/validate-all`
- [x] `Phase2Config` — добавить `SkillManifestConfig` (CurrentHerculesVersion, AllowedRiskLevels)
- [x] Unit-тесты: `SkillManifestTests.cs` — 32 теста: semver validation, Hercules-version compatibility, RequiredTools check, RiskLevel enforcement, Budget sanity, IsCompatible quick-check, full valid manifest
- [x] `dotnet build` проходит без warnings (NU1902 pre-existing)
- [x] `dotnet test` проходит (649/656; 7 pre-existing failures: OtelService, BudgetGuard, WASM sandbox)

## Scope / Likely files
src/agent/Skills/Package/Manifest.cs

## Dependencies
- блокирует / опирается на: [task_019 — skill-package-format](task_019.md)

## Risks / Rollback
Schema drift; нужна валидация по версии схемы.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Skills/Package/SkillManifest.cs`** — manifest record:
- SchemaVersion, Owner, Min/MaxHerculesVersion (semver)
- InputSchemaVersion, OutputSchemaVersion (semver)
- RequiredTools (List<string>), Permissions (List<string>)
- ModelRequirements (string), RiskLevel (SkillRiskLevel enum: Low/Medium/High/Critical)
- Budget (SkillManifestBudget: MaxTokensPerCall, MaxCallsPerMinute, MaxCostPerCallUsd)

**`Skills/Package/SkillManifestValidator.cs`** — валидация:
- `Validate(SkillManifest?)` → `SkillManifestValidationResult` с Errors, Warnings, MissingTools, IsCompatible
- Semver regex validation (strict RFC -compliant)
- Hercules version compatibility check (Min ≤ current ≤ Max)
- RequiredTools check against known tools (empty-known = offline fallback)
- RiskLevel enforcement against AllowedRiskLevels
- Budget sanity (non-negative values)
- `IsCompatible(SkillManifest?)` — quick boolean check

**`Storage/Models.cs`** — `SkillMeta` расширен:
- Owner, MinHerculesVersion, MaxHerculesVersion, InputSchemaVersion, OutputSchemaVersion
- Permissions, ModelRequirements, RiskLevel (int), Budget (SkillMetaBudget)
- `SkillMetaBudget` record (MaxTokensPerCall, MaxCallsPerMinute, MaxCostPerCallUsd)

**`Skills/SkillPackageManifest.cs`** — `SkillPackageSkillMeta` расширен:
- Owner, MinHerculesVersion, MaxHerculesVersion, InputSchemaVersion, OutputSchemaVersion
- Permissions, ModelRequirements, RiskLevel, Budget (SkillPackageBudget)

**`Skills/SkillPackager.cs`** — Export/Import propagation:
- Export: все manifest-поля копируются из SkillMeta → SkillPackageSkillMeta/SkillPackageBudget
- Import: все manifest-поля копируются из SkillPackageSkillMeta → SkillMeta

**`Config/AppConfig.cs`** — `SkillManifestConfig` + `Phase2Config.SkillManifest`:
- CurrentHerculesVersion (default "1.0.0")
- AllowedRiskLevels (List<int>, default {0, 1, 2})

**`Hercules.WebApi/Controllers/SkillManifestController.cs`** — 3 эндпоинта:
- `GET /api/skills/{id}/manifest` — получить манифест
- `POST /api/skills/{id}/manifest/validate` — валидировать совместимость (опционально: knownTools в body)
- `POST /api/skills/manifest/validate-all` — валидировать все навыки

**DI** (CLI + WebAPI):
- `Phase2Config` зарегистрирован в DI
- `SkillManifestConfig` singleton
- `SkillManifestValidator` singleton (CurrentHerculesVersion + AllowedRiskLevels из конфига)

**Tests** — `tests/.../Skills/Package/SkillManifestTests.cs` — 32 теста:
- Semver validation (11 cases: valid/invalid formats)
- Hercules-version compatibility (4 cases)
- RequiredTools check (4 cases)
- RiskLevel enforcement (3 cases)
- Budget sanity (3 cases)
- Schema version errors (2 cases)
- IsCompatible quick-check (3 cases)
- Full valid manifest (1 case)

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 649/656 passed (7 pre-existing: OtelService, BudgetGuard, WASM sandbox)
