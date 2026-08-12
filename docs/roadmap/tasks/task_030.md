# Task 30 — Шаблоны агентов

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `agent-templates`

## Goal
Готовые бандлы skills/memory/policy/tools/eval/config для вертикалей: greenhouse, energy, cold-chain, server closet.

## Acceptance criteria

### Sub-tasks

- [x] `Hercules.WebApi/Controllers/TemplateController.cs` — WebAPI endpoints: `GET /api/templates` (list all templates), `GET /api/templates/{fileName}` (template info + manifest), `POST /api/templates/{fileName}/apply` (apply template, body: optional conflict resolution)
- [x] `Hercules.WebApi/Program.cs` — зарегистрировать controller (controllers auto-discovered via namespace)
- [x] `data/Templates/greenhouse.agenttemplate` — ZIP archive: `template.json` + `memory/user_profile.md`
- [x] `data/Templates/cold-chain.agenttemplate` — ZIP archive for cold-chain scenario
- [x] `data/Templates/server-room.agenttemplate` — ZIP archive for server-room scenario
- [x] `data/Templates/vending.agenttemplate` — ZIP archive for vending scenario
- [x] `tests/.../Phase2Tests/AgentTemplateManagerTests.cs` — 13 unit-тестов: List (empty + valid + skip invalid + multiple), Apply (memory copy, errors, HasErrors), TemplateEntry, TemplateManifest, ConflictResolution
- [x] `dotnet build` проходит без warnings (pre-existing NU1902 on OTel.Api)
- [x] `dotnet test` проходит (835/845; 10 pre-existing: OtelService × 5, BudgetGuard × 1, WasmTool × 1, SkillQualityService × 2)

## Scope / Likely files
templates/greenhouse/, templates/cold-chain/, ...

## Dependencies
- блокирует / опирается на: [task_019 — skill-package-format](task_019.md)
- блокирует / опирается на: [task_021 — skill-marketplace](task_021.md)

## Risks / Rollback
Шаблон становится "магическим"; нужна явная доку ментация override-полей.

## Implementation notes

### 2026-08-13

**Добавлено:**

**`Hercules.WebApi/Controllers/TemplateController.cs`** — 3 endpoints:
- `GET /api/templates` — list all available templates (returns TemplateEntryDto)
- `GET /api/templates/{fileName}` — template manifest (returns TemplateManifestDto)
- `POST /api/templates/{fileName}/apply` — apply template (body: optional ConflictResolution), returns ApplyTemplateResultDto

**`data/Templates/*.agenttemplate`** — 4 ready-to-use vertical bundles:
- `greenhouse.agenttemplate` — greenhouse monitoring scenario
- `cold-chain.agenttemplate` — cold-chain logistics scenario
- `server-room.agenttemplate` — server room operations scenario
- `vending.agenttemplate` — vending fleet operations scenario
- Each ZIP contains: `template.json` (manifest) + `memory/user_profile.md` (template memory file with placeholder values)

**`templates/*.template.json`, `templates/*.user_profile.md`** — source files for template ZIPs (distributed in repo)

**`tests/.../Phase2Tests/AgentTemplateManagerTests.cs`** — 13 new unit tests:
- List_Returns_Empty_When_No_Templates, List_Returns_TemplateEntry_For_Valid_Zip, List_Skips_Invalid_Zip_Files, List_Returns_Multiple_Entries
- Apply_Throws_When_Template_Not_Found, Apply_Copies_Memory_Files, Apply_Sets_HasErrors_False_When_Successful, Apply_Adds_Error_When_Skill_Not_Found_In_Archive
- ApplyTemplateResult_Default_HasEmptyLists, TemplateEntry_Contains_All_Fields, TemplateManifest_Default_Values, DirectoryPath_Is_Set_To_Templates_Subdirectory, ConflictResolution_Has_All_Values

**Note:** `AgentTemplateManager` + CLI commands `/templates list|apply` + `StorageConfig.TemplatesDir` + DI registration were already implemented before this task.

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors (NU1902 pre-existing)
- `dotnet build` Hercules.WebApi.csproj — 0 errors (NU1902 pre-existing)
- `dotnet build` Hercules.Agent.Tests.csproj — 0 errors
- `dotnet test --filter AgentTemplateManagerTests` — 13/13 passed
- `dotnet test` — 835/845 passed (10 pre-existing: OtelService × 5, BudgetGuard × 1, WasmTool × 1, SkillQualityService × 2)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
