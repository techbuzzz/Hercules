# Task 32 — Манифест агента

**Phase:** 3
**Status:** done
**Owner:** —
**Slug:** `agent-manifest`

## Goal
Каждый агент публикует agent.manifest.json: name, version, capabilities, skills, endpoint, auth, поддерживаемые версии протокола, resource limits, trust metadata.

## Acceptance criteria

### Sub-tasks

- [x] `AgentManifest` — добавить поля: `SupportedProtocolVersions` (List<string>, snake_case JSON), `ResourceLimits` (ManifestResourceLimits), `TrustMetadata` (ManifestTrustMetadata), `Skills` (List<ManifestSkillEntry>)
- [x] `ManifestResourceLimits` record — `MaxTokensPerRequest`, `MaxConcurrentRequests`, `MaxToolCallsPerRequest`, `MaxWallClockSecondsPerRequest`, `MaxCostPerDayUsd`, `MaxTokensPerDay` (snake_case JSON)
- [x] `ManifestTrustMetadata` record — `Level` (trusted/verified/unverified), `IdentityProvider`, `IdentityClaims`, `VerifiedBy`, `VerifiedAt`
- [x] `ManifestSkillEntry` record — Id, Name, Description, Version, RiskLevel, Tools, PhraseReceivers, CreatedAt, UpdatedAt
- [x] `AgentManifestService` — extended constructor с параметрами для protocol versions, resource limits, trust metadata, skillsProvider; `SaveAsync` async-метод; `Current` корректно пробрасывает статические поля из конфига
- [x] `MeshConfig` — добавить `SupportedProtocolVersions`, `ResourceLimits` (ManifestResourceLimitsConfig), `TrustMetadata` (ManifestTrustMetadataConfig)
- [x] `MeshServiceExtensions` — DI registration передаёт config-значения в `AgentManifestService`; `ManifestCapabilitiesProvider.GetSkills()` → `ManifestSkillEntry`
- [x] Startup persistence — CLI Program.cs и WebAPI Program.cs сохраняют манифест на старте с валидацией и логированием ошибок
- [x] CLI `/manifest show|refresh|validate` — команда в ConsoleUI с табличным выводом manifest summary, capabilities и ошибок валидации
- [x] Unit-тесты `Phase3Tests/AgentManifestTests.cs` — +7 тестов: protocol versions, resource limits, trust metadata, skills population, SaveAsync, empty protocol versions validation, JSON serialization
- [x] `dotnet build` проходит без warnings (NU1902 pre-existing на OTel.Api)
- [x] `dotnet test` проходит (872/880; 8 pre-existing failures: OtelService × 5, BudgetGuard × 1, WasmTool × 1, AgentCore × 1)

## Scope / Likely files
src/agent/Mesh/Manifest/, src/agent/Hercules.WebApi/Controllers/ManifestController.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)

## Risks / Rollback
Расхождение манифеста и реальности; генерация из runtime-state.

## Implementation notes

### 2026-08-13

**`src/agent/Mesh/AgentManifest.cs`** — extended types:
- `ManifestResourceLimits` — 6 полей с `[JsonPropertyName]` snake_case, backward-compatible
- `ManifestTrustMetadata` — Level, IdentityProvider, IdentityClaims, VerifiedBy, VerifiedAt
- `ManifestSkillEntry` — расширенная запись навыка (Id, Name, Version, RiskLevel, Tools, PhraseReceivers, CreatedAt, UpdatedAt)
- `AgentManifestService` — extended constructor + backward-compatible basic constructor (this(...) chaining); `Current` корректно пробрасывает статические поля: только перезаписывает если backing field ≠ null (чтобы basic constructor сохранял значения из extended chain)
- `SaveAsync` — async-версия для DI-friendly startup use

**`src/agent/Config/AppConfig.cs`** — `MeshConfig`:
- `SupportedProtocolVersions` (List<string>, default ["1.0"])
- `ManifestResourceLimitsConfig` — 6 полей лимитов
- `ManifestTrustMetadataConfig` — Level, IdentityProvider, IdentityClaims, VerifiedBy

**`src/agent/Mesh/MeshServiceExtensions.cs`**:
- `ManifestCapabilitiesProvider.GetSkills()` → `List<ManifestSkillEntry>` из SkillManager
- DI registration: AgentManifestService получает supportedProtocolVersions, resourceLimits, trustMetadata из MeshConfig
- Skills provider подключен через `capsProvider.GetSkills`

**Startup persistence** (CLI + WebAPI):
- `Program.cs` (CLI): после MCP init → manifestService.Save() + Validate() + логирование ошибок/валидации
- `Program.cs` (WebAPI): после `app.MapMesh()` → manifestService.Save() + Validate() + логирование

**CLI `/manifest`** (ConsoleUI):
- `/manifest show` — таблица manifest summary + capabilities (Spectre Table)
- `/manifest refresh` — async SaveAsync + статус
- `/manifest validate` — Validate() + ошибки/валидация
- PrintHelp обновлён

**Tests** — `tests/.../Phase3Tests/AgentManifestTests.cs` +7 тестов (13 total):
- ExtendedConstructor_Populates_SupportedProtocolVersions
- ExtendedConstructor_Populates_ResourceLimits
- ExtendedConstructor_Populates_TrustMetadata
- ExtendedConstructor_Populates_Skills
- SaveAsync_Writes_Manifest_To_File
- Validate_Returns_Error_For_Empty_ProtocolVersions
- Save_Includes_ProtocolVersions_ResourceLimits_TrustMetadata_And_Skills

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet test` — 872/880 passed (8 pre-existing failures: OtelService × 5, BudgetGuard × 1, WasmTool × 1, AgentCore × 1)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
