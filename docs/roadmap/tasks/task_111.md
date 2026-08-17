# Task 111 — Produces<T>() + DTO рефакторинг (исключить анонимные типы)

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `produces-dto-refactor`
**Studio Stage:** 0 (pre-req для качественного codegen)

## Goal
Добавить `.Produces<T>(statusCode)` на каждый endpoint для типизированных response schemas в OpenAPI. Рефакторить все анонимные `Results.Ok(new { ... })` в named DTOs. Создать чистый DTO слой. Полностью исключить анонимные типы из API responses.

## Acceptance criteria
- [ ] Каждый `MapGet/MapPost/MapPut/MapPatch/MapDelete` имеет `.Produces<T>(statusCode)` для success responses
- [ ] Error responses: `.ProducesProblem(StatusCodes.Status400BadRequest)` для валидации, `.ProducesProblem(404)` для not found
- [ ] Все анонимные `Results.Ok(new { ... })` → рефакторить в named DTOs
- [ ] DTOs размещены в Dedicated contracts: `src/agent/Hercules.WebApi/Contracts/` (новая папка)
- [ ] Каждый DTO — `public sealed record` с proper naming (PascalCase, суффикс `Dto`/`Request`/`Response`)
- [ ] Minimal API стиль сохранён — НЕ использовать `ControllerBase`
- [ ] OpenAPI документ содержит полные response schemas (не `object`) для всех endpoints
- [ ] `dotnet build` + `dotnet test` pass

## DTO placement strategy

```
src/agent/Hercules.WebApi/Contracts/
├── Chat/          — ChatRequest, ChatResponseDto (moved from Agent/WebApiAdapter.cs)
├── Skills/        — SkillDto, SkillDetailDto, CreateSkillRequest, UpdateSkillRequest, SkillEvaluationResultDto
├── Mesh/          — MeshAgentDto, MeshHealthDto, MeshDashboardDto, etc. (moved from Mesh/Dashboard/)
├── Config/        — ConfigDto
├── Memory/        — MemoryProfileDto, etc.
├── Stats/         — StatsDto, BudgetDto, etc.
├── Tools/         — ToolEntryDto, McpServerDto, etc.
├── Lifecycle/     — LifecycleInventoryDto, etc.
├── Audit/         — AuditEntryDto, AuditLogDto
├── Mesh/          — (moved from Mesh/Dashboard/MeshDashboardDtos.cs)
└── Common/        — HealthResponseDto, ErrorResponseDto, PaginatedResultDto<T>
```

## Migration plan (по доменам)

### Приоритет 1 — ключевые домены (Studio Stage 0-3)
- [ ] Chat: `ChatRequest`, `ChatResponseDto` → `Contracts/Chat/`
- [ ] Skills: `SkillDto`, `SkillDetailDto`, `CreateSkillRequest`, `UpdateSkillRequest` → `Contracts/Skills/`
- [ ] Config: `ConfigDto` → `Contracts/Config/`
- [ ] Stats: `StatsDto`, `BudgetDto`, `BudgetMonthlyDto` → `Contracts/Stats/`
- [ ] Memory: profile/reset responses → `Contracts/Memory/`
- [ ] Mesh: все Mesh DTOs → `Contracts/Mesh/` (consolidate from Mesh/Dashboard/)

### Приоритет 2 — операционные домены (Studio Stage 4-6)
- [ ] Tools: tool registry DTOs → `Contracts/Tools/`
- [ ] MCP: MCP server DTOs → `Contracts/Tools/`
- [ ] Audit: `AuditEntryDto`, `AuditLogDto` → `Contracts/Audit/`
- [ ] Lifecycle: inventory/health DTOs → `Contracts/Lifecycle/`
- [ ] LLM: health/capabilities/config DTOs → `Contracts/LLM/`
- [ ] Quotas: quota DTOs → `Contracts/Quotas/`

### Приоритет 3 — остальные домены
- [ ] A2A, Backups, FleetTemplates, Grants, Simulation, SLOs, Approvals, Escalations, Observability, SelfImprovement, Tasks, Context, Cache, Rollout, SkillManifest, SkillHarness, SkillQuality, SkillLifecycle, Marketplace, Templates

## Anonymous type → named DTO examples

```csharp
// Before
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", version = "1.0" }));

// After
app.MapGet("/api/health", () => Results.Ok(new HealthResponseDto("ok", "1.0")))
   .Produces<HealthResponseDto>(StatusCodes.Status200OK);

// Before
app.MapPost("/api/memory/reset", () => Results.Ok(new { status = "ok" }));

// After
app.MapPost("/api/memory/reset", () => Results.Ok(new StatusResponseDto("ok")))
   .Produces<StatusResponseDto>(StatusCodes.Status200OK);
```

## Dependencies
- task_109 (AddOpenApi — нужен для проверки schemas)
- task_110 (WithTags — можно делать параллельно)
- task_112 (WithName — можно делать параллельно)

## Scope / Likely files
- New: `src/agent/Hercules.WebApi/Contracts/` (все DTOs)
- Modified: все 35 controllers (добавить .Produces<T>())
- Modified: `src/agent/Agent/WebApiAdapter.cs` (DTOs перемещаются в Contracts)
- Modified: `src/agent/Mesh/Dashboard/MeshDashboardDtos.cs` (перемещается в Contracts/Mesh/)

## Risks / Rollback
- **Breaking change:** DTOs перемещаются между namespace. Если CLI или другие consumers ссылаются на `Hercules.Agent.ChatResponseDto`, нужно update usings.
- **Mitigation:** Добавить `using` aliases или keep old types as type-forwarding wrappers временно.
- Большой объём механической работы (~195 endpoints × 1-3 строки + ~40 анонимных returns → named DTOs)

## Notes
- Это самая трудозатратная задача в pipeline (~1-2 дня)
- Можно делать по доменам параллельно с task_110/112
- Рекомендуется делать в отдельной ветке `feat/openapi-dto-contracts`

## Links
- Backlog: [../backlog.md](../backlog.md)
- Studio SDK types: [../../../src/hercules-studio/renderer/src/sdk/types.ts](../../../src/hercules-studio/renderer/src/sdk/types.ts) (будут заменены generated)
- Web-UI api.ts: [../../../src/hercules-web/src/lib/api.ts](../../../src/hercules-web/src/lib/api.ts) (будут заменены generated)