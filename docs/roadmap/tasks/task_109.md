# Task 109 — AddOpenApi() в Program.cs

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `add-openapi-producer`
**Studio Stage:** 0 (pre-req для task_113/115)

## Goal
Добавить встроенный в .NET 10 OpenAPI producer (`Microsoft.AspNetCore.OpenApi`) в Hercules.WebApi, чтобы агент публиковал `/openapi/v1.json` со всеми endpoints, DTOs, operationId, tags и response schemas.

## Acceptance criteria
- [ ] `builder.Services.AddOpenApi()` в `Hercules.WebApi/Program.cs` (до `app.Build()`)
- [ ] `app.MapOpenApi()` после `app.Build()` (публикует `/openapi/v1.json`)
- [ ] OpenAPI документ доступен на `http://localhost:8421/openapi/v1.json` при запуске агента
- [ ] Документ содержит все ~195 endpoints из 35 контроллеров
- [ ] Документ содержит operationId (из `WithName`) для 33 контроллеров (Marketplace/Template — task_112)
- [ ] JSON валиден (проверка через https://editor.swagger.io или `npx @redocly/cli lint`)
- [ ] `dotnet build` + `dotnet test` pass
- [ ] Manual smoke: `dotnet run --project src/agent/Hercules.WebApi` → `curl http://localhost:8421/openapi/v1.json` → 200 OK + valid JSON

## Dependencies
- нет (стартовая задача для OpenAPI pipeline)

## Scope / Likely files
src/agent/Hercules.WebApi/Program.cs

## Notes
- `Microsoft.AspNetCore.OpenApi` встроен в .NET 10 SDK, NuGet не нужен
- OpenAPI 3.1 (built-in), не 3.0 (Swashbuckle)
- Без `Produces<T>()` (task_111) response schemas будут пустыми/`object` — это нормально для старта, task_111 добавит типы
- `/openapi/v1.json` не требует X-Api-Key (документация публична)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Epic Studio: [../EPIC_Hercules_Studio/README.md](../EPIC_Hercules_Studio/README.md)