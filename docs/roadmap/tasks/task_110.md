# Task 110 — WithTags на все контроллеры

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `withtags-all-controllers`
**Studio Stage:** 0 (pre-req для Orval tags-split)

## Goal
Добавить `.WithTags("Domain")` на каждый endpoint во всех 35 контроллерах. Теги группируют endpoints по доменам в OpenAPI документе, что позволяет Orval `tags-split` mode генерировать per-domain TS файлы (SkillsApi.ts, MeshApi.ts, ConfigApi.ts, etc.).

## Acceptance criteria
- [ ] Каждый `MapGet/MapPost/MapPut/MapPatch/MapDelete` во всех 35 контроллерах имеет `.WithTags("Domain")`
- [ ] Теги консистентны с именами контроллеров (см. таблицу ниже)
- [ ] Minimal API стиль сохранён — НЕ использовать `ControllerBase`, только `MapXxx` extension methods
- [ ] OpenAPI документ (`/openapi/v1.json`) содержит теги в `tags` array и каждый operation имеет правильный `tags`
- [ ] `dotnet build` + `dotnet test` pass

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

## Dependencies
- task_109 (AddOpenApi — нужен чтобы видеть теги в документе)

## Scope / Likely files
Все 35 файлов в `src/agent/Hercules.WebApi/Controllers/`

## Notes
- 4 контроллера уже имеют WithTags: McpController, MeshObservabilityController, RolloutController, ToolRegistryController
- 31 контроллер нужно добавить .WithTags() на каждый MapXxx call
- Важно: Minimal API стиль — `.WithTags()` chain method, не атрибуты

## Links
- Backlog: [../backlog.md](../backlog.md)