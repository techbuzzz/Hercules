# Task 110 — WithTags на все контроллеры

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `withtags-all-controllers`
**Studio Stage:** 0 (pre-req для Orval tags-split)

## Goal
Добавить `.WithTags("Domain")` на каждый endpoint во всех 35 контроллерах. Теги группируют endpoints по доменам в OpenAPI документе, что позволяет Orval `tags-split` mode генерировать per-domain TS файлы (SkillsApi.ts, MeshApi.ts, ConfigApi.ts, etc.).

## Acceptance criteria
- [x] Каждый `MapGet/MapPost/MapPut/MapPatch/MapDelete` во всех контроллерах покрыт тегом
- [x] Теги консистентны с именами контроллеров (см. таблицу ниже)
- [x] Minimal API стиль сохранён — `MapXxx` extension methods, `ControllerBase` не вводился
- [x] `openapi.json` содержит массив `tags` и **все 223 операции** имеют ровно один тег
- [x] `dotnet build` (0 errors) + `dotnet test` (2150 passed / 13 pre-existing failures, тот же baseline)

## What was actually done

Tag coverage is complete: **223 operations, 0 untagged**, 37 domain tags + the framework's
auto-tag `Hercules.WebApi` for the two non-API endpoints (`/` and `/agent-card.json`).

Breakdown of how the 222 endpoints in `Controllers/` are covered:

| How | Endpoints | Controllers |
|---|---|---|
| `.WithTags(...)` added by this task | 180 | 30 controllers |
| Already had per-endpoint `.WithTags(...)` | 20 | MeshObservability (5), TaskProgress (9), ToolRegistry (6) |
| Already covered by `app.MapGroup(...).WithTags(...)` | 22 | Mcp (3), Rollout (5), SecurityOps (6), System (8) |

The change is purely additive — 180 insertions / 180 deletions, no route string, handler
signature or return type touched.

### Corrections to the plan in this file

- **37 controller files, not 35.** `SecurityOpsController` and `SystemController` were added
  after this task was written and were missing from the mapping table. Both already tag via
  `MapGroup(...).WithTags(...)`, so they needed no per-endpoint tags — but they are now
  accounted for: `SecurityOpsController` → tag `Security`, `SystemController` → tag `System`.
- **Existing tags that did not match the table were normalised.** `MeshObservabilityController`
  used the tag `"Mesh.Observability"`. A dot in a tag becomes a dot in the filename Orval's
  `tags-split` mode generates, so it was changed to `"MeshObservability"` per the table.
  Verified: the only remaining dotted tag in the document is `Hercules.WebApi`, which the
  framework assigns to the two non-API endpoints.
- **4 controllers tag at group level, not per endpoint.** The original note said 4 controllers
  "already have WithTags" (implying per-endpoint). In fact Mcp, Rollout, SecurityOps and System
  tag once via `MapGroup("/api/...").WithTags("...")`, which covers all their endpoints.

### Pre-existing issue found (not fixed here)

`POST /api/skills/{id}/improve` is registered **twice**: once in `SkillsController`
(`.WithName("ImproveSkill").WithTags("Skills")`) and again in `SkillLifecycleController`
(`.WithName("ImproveSkillLifecycle").WithTags("SkillLifecycle")`). ASP.NET Core resolves a
duplicate route to the last registration, so the operation resolves to
`ImproveSkillLifecycle` / `SkillLifecycle`, and the `SkillsController` metadata is dead.
This is a route collision, not a tagging problem — left for a dedicated task.

## Validation
- `dotnet build src/agent/Hercules.slnx` → 0 errors
- `openapi.json`: 223 operations, 0 untagged, 38 entries in the `tags` array
- `dotnet test` → 2150 passed / 13 failed — identical to the pre-change baseline measured via a
  `git stash` round-trip (Redis, NATS, Otel, Wasmtime, numeric validator, eval, config-default
  tests that require external services)

## Dependencies
- task_109 (AddOpenApi — нужен чтобы видеть теги в документе) — done

## Scope / Likely files
30 файлов в `src/agent/Hercules.WebApi/Controllers/` (7 не тронуты: 4 group-tagged,
2 уже полностью покрыты per-endpoint, 1 — ToolRegistry)

## Notes
- Minimal API стиль — `.WithTags()` chain method, не атрибуты
- Порядок в цепочке: `.WithName("X").WithTags("Y")` — совпадает с уже существующим стилем
  в TaskProgressController и ToolRegistryController
- Трансформация выполнена скриптом с учётом строковых литералов, char-литералов и обоих
  видов комментариев; проверена diff'ом (180/180, только добавления) и сборкой

## Links
- Backlog: [../backlog.md](../../backlog.md)
- task_109 (OpenAPI producer): [completed/task_109.md](task_109.md)
## Tag mapping (35 контроллеров → 35 тегов)

| Controller | Tag |
|---|---|
| ChatController | Chat |
| SkillsController | Skills |
| SkillLifecycleController | SkillLifecycle |
| SkillQualityController | SkillQuality |
| SkillManifestController | SkillManifest |
| SkillHarnessController | SkillHarness |
| MemoryController | Memory |
| StatsController | Stats |
| ConfigController | Config |
| RolloutController | Rollout (already has) |
| MeshController | Mesh |
| MeshObservabilityController | MeshObservability (already has) |
| MeshProfileController | MeshProfiles |
| LifecycleController | Lifecycle |
| BudgetController | Budget |
| QuotasController | Quotas |
| AuditController | Audit |
| LlmController | LLM |
| A2AController | A2A |
| BackupController | Backups |
| FleetTemplateController | FleetTemplates |
| GrantsController | Grants |
| SimulationController | Simulation |
| SloController | SLOs |
| ApprovalController | Approvals |
| EscalationController | Escalations |
| ObservabilityController | Observability |
| SelfImprovementController | SelfImprovement |
| TaskProgressController | Tasks |
| ContextController | Context |
| CacheController | Cache |
| ToolRegistryController | Tools (already has) |
| McpController | MCP (already has) |
| MarketplaceController | Marketplace |
| TemplateController | Templates |
| SecurityOpsController | Security (group-level, already had) |
| SystemController | System (group-level, already had) |
