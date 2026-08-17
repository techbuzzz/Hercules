# Task 26 — Least-privilege grants

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `least-privilege-grants`

## Goal
Навык получает только объявленные capabilities. Runtime проверяет grant при вызове; импорт навыка не может тихо расширить разрешения.

## Acceptance criteria

### Sub-tasks

- [x] `LeastPrivilegeConfig` в `Config/AppConfig.cs`: `Enabled` (default true), `EnforceOnImport` (true), `EnforceOnRuntime` (true), `MigrationMode` (true), `AllowedPermissions` (list)
- [x] `SkillGrant` record + `GrantScope` enum + `GrantValidationResult` + `GrantRequest` в `Tools/Grants/Models.cs`
- [x] `ISkillGrantService` interface + `SkillGrantService` implementation: `GrantAsync`, `RevokeAsync`, `RevokeByIdAsync`, `GetGrantsAsync`, `GetEffectivePermissionsAsync`, `CheckPermissionAsync`, `CheckAllPermissionsAsync`
- [x] `SkillGrantStore` — SQLite persistence: таблица `skill_grants` (id, skill_id, permission, scope, session_id, grantor, granted_at, expires_at) + indexes
- [x] `ToolPolicyEngine` — интеграция `ISkillGrantService?`; при tool execution проверяет `CheckAllPermissionsAsync(skillId, requiredPermissions)` и возвращает Denied если нет grant
- [x] `SkillPackager` — при импорте: если `EnforceOnImport=true`, проверяет permissions навыка против `AllowedPermissions`; выбрасывает `InvalidOperationException` если ⊄
- [x] `GrantsController.cs` — WebAPI: `GET /api/grants/{skillId}`, `POST /api/grants`, `DELETE /api/grants/{skillId}`, `DELETE /api/grants/by-id/{id}`, `GET /api/grants/{skillId}/check`
- [x] `Program.cs` (CLI + WebAPI) — DI: `LeastPrivilegeConfig`, `SkillGrantStore`, `ISkillGrantService` → `SkillGrantService`; `ToolPolicyEngine` получает `ISkillGrantService`
- [x] `SkillGrantServiceTests.cs` — 14 unit-тестов: grant, revoke, scope priority, missing permissions, migration mode, effective permissions
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (776/783; 7 pre-existing: OtelService × 5, BudgetGuard × 1, WasmTool × 1)

## Implementation notes

### 2026-08-12

**Добавлено:**

**`src/agent/Tools/Grants/`** — 4 новых файла:
- `Models.cs` — `GrantScope` enum (Skill/Session/Global), `SkillGrant` record, `GrantValidationResult`, `GrantRequest`
- `ISkillGrantService.cs` — интерфейс с 7 методами
- `SkillGrantService.cs` — реализация: SQLite-backed, проверяет grants по scope priority (Global > Session > Skill), migration mode (no grant = Read по умолчанию)
- `SkillGrantStore.cs` — IDisposable, shared `SqliteConnection`, таблица `skill_grants` + 2 индекса

**`src/agent/Config/AppConfig.cs`** — `LeastPrivilegeConfig`:
- `Enabled`, `MigrationMode` (true), `EnforceOnImport` (true), `EnforceOnRuntime` (true), `AllowedPermissions` (list)

**`src/agent/Tools/Policy/ToolPolicy.cs`** — `PolicyContext.SkillId` (nullable string)

**`src/agent/Tools/Policy/ToolPolicyEngine.cs`** — интеграция:
- `ISkillGrantService?` nullable в конструкторе
- В `Evaluate()`: после permission check — grant check: `CheckAllPermissionsAsync(skillId, requiredPermissions)` → Denied если нет grant

**`src/agent/Skills/SkillPackager.cs`** — интеграция grant validation:
- Новый конструктор с `LeastPrivilegeConfig` + `ISkillGrantService`
- В `Import()`: если `EnforceOnImport=true` и permissions не ⊆ AllowedPermissions → `InvalidOperationException`

**`src/agent/Agent/AgentCore.cs`** — передача SkillId в PolicyContext:
- `RunWithToolsAsync` принимает `string? skillId` параметр
- `PolicyContext` получает `SkillId`
- `HandleAsyncCore` и `EvaluateSkillAsync` передают `route.MatchedSkill?.Meta.Id`

**`src/agent/Hercules.WebApi/Controllers/GrantsController.cs`** — 5 endpoints:
- `GET /api/grants/{skillId}` — grants + session filter
- `POST /api/grants` — выдать grant
- `DELETE /api/grants/{skillId}` — revoke
- `DELETE /api/grants/by-id/{id}` — revoke by grant ID
- `GET /api/grants/{skillId}/check` — check permission

**DI (CLI + WebAPI):** `LeastPrivilegeConfig`, `SkillGrantStore`, `ISkillGrantService` → `SkillGrantService`; `ToolPolicyEngine` получает `ISkillGrantService`

**Tests:** `tests/.../Tools/Grants/SkillGrantServiceTests.cs` — 14 тестов

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors, 0 warnings
- `dotnet build Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet test` — 776/783 passed (7 pre-existing: OtelService × 5, BudgetGuard × 1, WasmTool × 1)

## Scope / Likely files
src/agent/Tools/Grants/, src/agent/Skills/

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)
- блокирует / опирается на: [task_020 — skill-manifest](task_020.md)

## Risks / Rollback
Обратная совместимость со старыми навыками; миграционный режим.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
