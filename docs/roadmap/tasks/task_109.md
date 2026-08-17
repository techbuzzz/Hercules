# Task 109 — OpenAPI producer + Scalar UI + Build-Time Generation

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `add-openapi-producer`
**Studio Stage:** 0 (pre-req для task_113/115)

## Goal
Добавить встроенный в .NET 10 OpenAPI producer (`Microsoft.AspNetCore.OpenApi`) в Hercules.WebApi:
1. Runtime: `/openapi/v1.json` — OpenAPI 3.1 документ при запуске агента
2. Scalar UI: `/scalar` — интерактивная документация API (тест endpoints из браузера)
3. Build-Time: `openapi.json` генерируется при `dotnet build` → коммитится в git → виден в PR diff
4. XML doc comments → OpenAPI descriptions автоматически

## Acceptance criteria

### Runtime OpenAPI
- [ ] `builder.Services.AddOpenApi()` в `Hercules.WebApi/Program.cs` (до `app.Build()`)
- [ ] `app.MapOpenApi()` после `app.Build()` (публикует `/openapi/v1.json`)
- [ ] OpenAPI документ доступен на `http://localhost:8421/openapi/v1.json` при запуске агента
- [ ] Документ содержит все ~195 endpoints из 35 контроллеров
- [ ] Документ содержит operationId (из `WithName`) для 33 контроллеров (Marketplace/Template — task_112)
- [ ] `/openapi/v1.json` не требует X-Api-Key (документация публична)

### Scalar UI
- [ ] `Scalar.AspNetCore` NuGet package добавлен в `Hercules.WebApi.csproj`
- [ ] `app.MapScalarApiReference()` после `app.MapOpenApi()` (публикует `/scalar`)
- [ ] Scalar UI доступен на `http://localhost:8421/scalar` при запуске агента
- [ ] Scalar UI показывает все endpoints с tags, summaries, descriptions
- [ ] Scalar UI позволяет тестировать endpoints (try-it-out)

### Build-Time Generation
- [ ] `Microsoft.Extensions.ApiDescription.Server` NuGet package добавлен в `Hercules.WebApi.csproj`
- [ ] `<OpenApiDocumentsDirectory>.</OpenApiDocumentsDirectory>` в `.csproj` PropertyGroup
- [ ] `<OpenApiGenerateDocumentsOptions>--file-name openapi</OpenApiGenerateDocumentsOptions>` в `.csproj` PropertyGroup
- [ ] `dotnet build` генерирует `openapi.json` в корне `Hercules.WebApi/` проекта
- [ ] `openapi.json` коммитится в git (НЕ в .gitignore)
- [ ] Build-time detection: `var isBuildTime = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";` в Program.cs
- [ ] Условное исключение сервисов требующих config при build-time (DB, API keys, OpenTelemetry):
  ```csharp
  if (!isBuildTime) {
      // builder.AddOpenTelemetry();
      // builder.Services.AddDbContext(...);
      // etc.
  }
  ```

### XML Documentation Comments
- [ ] `<GenerateDocumentationFile>true</GenerateDocumentationFile>` в `.csproj` PropertyGroup
- [ ] `<NoWarn>$(NoWarn);1591;1573</NoWarn>` в `.csproj` PropertyGroup (подавить missing-doc warnings)
- [ ] XML doc comments `/// <summary>` на handler methods (где имеет смысл, постепенно)
- [ ] XML doc comments на model-bound parameters (DTOs), НЕ на DI-injected parameters
- [ ] Handler methods `internal` или `public` (не `private` — XML docs не подхватываются)

### Validation
- [ ] JSON валиден: `npx @redocly/cli lint src/agent/Hercules.WebApi/openapi.json` (или Spectral — task_117)
- [ ] `dotnet build` генерирует `openapi.json` без ошибок
- [ ] `dotnet build` + `dotnet test` pass
- [ ] Manual smoke: `dotnet run --project src/agent/Hercules.WebApi` → `curl http://localhost:8421/openapi/v1.json` → 200 OK + valid JSON
- [ ] Manual smoke: Scalar UI на `http://localhost:8421/scalar` → видны endpoints, tags, можно тестировать

## Implementation details

### .csproj changes

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <LangVersion>latest</LangVersion>
  <RootNamespace>Hercules.WebApi</RootNamespace>
  <AssemblyName>Hercules.WebApi</AssemblyName>
  <InvariantGlobalization>true</InvariantGlobalization>

  <!-- OpenAPI build-time generation -->
  <OpenApiDocumentsDirectory>.</OpenApiDocumentsDirectory>
  <OpenApiGenerateDocumentsOptions>--file-name openapi</OpenApiGenerateDocumentsOptions>

  <!-- XML documentation comments → OpenAPI descriptions -->
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);1591;1573</NoWarn>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Scalar.AspNetCore" Version="*" />
  <PackageReference Include="Microsoft.Extensions.ApiDescription.Server" Version="*" />
</ItemGroup>
```

### Program.cs changes

```csharp
// OpenAPI document generation (built-in .NET 10, OpenAPI 3.1)
builder.Services.AddOpenApi();

// Build-time detection: app is launched by GetDocument.Insider during dotnet build
var isBuildTime = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

if (!isBuildTime)
{
    // Services requiring config (DB connection, API keys, OpenTelemetry)
    // builder.AddOpenTelemetry();
    // builder.Services.AddDbContext<HerculesDbContext>(...);
}

var app = builder.Build();

// Runtime OpenAPI document at /openapi/v1.json
app.MapOpenApi();

// Scalar UI at /scalar (interactive API documentation + testing)
app.MapScalarApiReference();

app.Run();
```

### Workflow после task_109

```
Developer changes API (adds endpoint, DTO, .Produces<T>())
  ↓
dotnet build
  ↓
openapi.json regenerated in Hercules.WebApi/ (build-time)
  ↓
git add openapi.json && git commit
  ↓
PR review: openapi.json diff shows contract changes ← KEY BENEFIT
  ↓
CI: dotnet build (regenerates openapi.json) + dotnet test
CI: npm run gen:api (Studio + Web-UI regen from openapi.json)
```

### Sources for TS codegen

After task_109, TS codegen (task_113/115) can use TWO sources:
1. **Build-time:** `src/agent/Hercules.WebApi/openapi.json` (committed to git, no running agent needed) — для CI
2. **Runtime:** `http://localhost:8421/openapi/v1.json` (running agent) — для dev

Both produce the same document. Build-time is preferred for CI, runtime for dev iteration.

## Dependencies
- нет (стартовая задача для OpenAPI pipeline)

## Scope / Likely files
- `src/agent/Hercules.WebApi/Program.cs` — AddOpenApi, MapOpenApi, MapScalarApiReference, isBuildTime
- `src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — packages, properties, OpenApiGenerateDocuments
- New: `src/agent/Hercules.WebApi/openapi.json` — generated at build time, committed to git

## Notes
- `Microsoft.AspNetCore.OpenApi` встроен в .NET 10 SDK, NuGet не нужен для core OpenAPI
- `Scalar.AspNetCore` — отдельный NuGet для Scalar UI
- `Microsoft.Extensions.ApiDescription.Server` — отдельный NuGet для build-time generation
- OpenAPI 3.1 (built-in), не 3.0 (Swashbuckle)
- Без `Produces<T>()` (task_111) response schemas будут пустыми/`object` — это нормально для старта, task_111 добавит типы
- `openapi.json` в корне проекта — не в `bin/`, не в `.gitignore`
- Build-time detection (`GetDocument.Insider`) — официальная рекомендация от Microsoft
- CS1591 (missing XML doc on public member) и CS1573 (missing param tag) — подавляем, т.к. не все public members нуждаются в OpenAPI docs

## Reference
- Article: https://dev.to/nausaf/openapi-in-net-from-setup-to-build-time-generation-with-scalar-ui-1bio
- MS Docs AddOpenApi: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi
- MS Docs build-time: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi#customize-runtime-behavior-during-build-time-document-generation
- Scalar: https://scalar.com/
- MS Docs metadata: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata

## Links
- Backlog: [../backlog.md](../backlog.md)
- Epic Studio: [../EPIC_Hercules_Studio/README.md](../EPIC_Hercules_Studio/README.md)
- task_110 (WithTags): [task_110.md](task_110.md)
- task_111 (Produces+DTO): [task_111.md](task_111.md)
- task_112 (WithName): [task_112.md](task_112.md)
- task_113 (Studio codegen): [task_113.md](task_113.md)
- task_117 (Spectral lint): [task_117.md](task_117.md)