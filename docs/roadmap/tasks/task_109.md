# Task 109 — OpenAPI producer + Scalar UI + Build-Time Generation

**Phase:** 8
**Status:** in_progress
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

## Acceptance criteria (current state)

### Runtime OpenAPI
- [x] `builder.Services.AddOpenApi()` в `Hercules.WebApi/Program.cs` (до `app.Build()`)
- [x] `app.MapOpenApi()` после `app.Build()` (публикует `/openapi/v1.json`)
- [x] `app.MapScalarApiReference()` (публикует `/scalar`)
- [x] `OpenApiOptions.ShouldInclude = _ => true` (opts all minimal API endpoints into the document)
- [x] `AddEndpointsApiExplorer()` (bridge for build-time tool's MVC discovery)
- [x] `/openapi/v1.json` не требует X-Api-Key (путь вне `/api/*` — `ApiKeyMiddleware` пропускает)
- [x] `/scalar` не требует X-Api-Key (тот же bypass)

### Build-Time Generation
- [x] `Scalar.AspNetCore` NuGet package (`Hercules.WebApi.csproj`)
- [x] `Microsoft.AspNetCore.OpenApi` NuGet package
- [x] `Microsoft.Extensions.ApiDescription.Server` NuGet package
- [x] `<OpenApiDocumentsDirectory>.</OpenApiDocumentsDirectory>` в `.csproj`
- [x] `<OpenApiGenerateDocumentsOptions>--file-name openapi</OpenApiGenerateDocumentsOptions>` в `.csproj`
- [x] `dotnet build` генерирует `openapi.json` в корне `Hercules.WebApi/` проекта
- [x] Build-time detection: `var isBuildTime = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";` в Program.cs
- [x] Условное исключение сервисов требующих config при build-time:
  - `CodeExecutionTool` registration (ambiguous constructors trip DI validation)
  - ApiKeyStore key generation
  - `WebApiAdapter.EnsureSessionStarted()` (DB touch)
  - Agent manifest publishing
  - Agent card publishing
  - Tool registry discovery
  - MCP client initialization
  - `app.Run()` (blocks; не нужен при build-time)

### XML Documentation Comments
- [x] `<GenerateDocumentationFile>true</GenerateDocumentationFile>` в `.csproj`
- [x] `<NoWarn>$(NoWarn);1591;1573</NoWarn>` в `.csproj`
- [x] Подавлены CS1591/CS1573 warnings (XML docs постепенно, не все public members нуждаются)

### Validation
- [x] `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → 0 errors
- [x] `dotnet build` writes `openapi.json` (build-time target runs successfully)
- [~] `openapi.json` содержит все ~195 endpoints — **НЕ выполнено** (см. [Known limitations](#known-limitations))
- [ ] JSON валиден: `npx @redocly/cli lint` или Spectral (task_117) — deferred until document is populated
- [ ] Manual smoke: `curl http://localhost:8421/openapi/v1.json` → 200 OK — **blocked** by pre-existing `CodeExecutionTool` constructor ambiguity (см. [Known limitations](#known-limitations))
- [ ] Manual smoke: Scalar UI на `http://localhost:8421/scalar` — same blocker

## Sub-tasks
- [x] Добавить NuGet packages: `Scalar.AspNetCore`, `Microsoft.AspNetCore.OpenApi`, `Microsoft.Extensions.ApiDescription.Server`
- [x] Обновить `.csproj`: `OpenApiDocumentsDirectory`, `OpenApiGenerateDocumentsOptions`, `GenerateDocumentationFile`, `NoWarn`
- [x] Обновить `Program.cs`: `AddOpenApi(options => { ShouldInclude = _ => true })`, `AddEndpointsApiExplorer()`
- [x] Build-time detection: `var isBuildTime = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";`
- [x] Обернуть side-effecting post-build код в `if (!isBuildTime)`:
  - `ApiKeyStore.LoadOrGenerate` (file I/O)
  - `WebApiAdapter.EnsureSessionStarted` (SQLite touch)
  - Agent manifest / agent card publishing (file I/O)
  - Tool registry discovery (filesystem I/O)
  - MCP client init (network I/O)
  - `app.Run()` (blocks)
- [x] Обернуть `CodeExecutionTool` registration в `if (!isBuildTime)` (ambiguous constructors → DI validation fails for build-time tool host)
- [x] `app.MapOpenApi()` + `app.MapScalarApiReference()` после всех `app.MapXxx()` (route order)
- [x] Build verified: `dotnet build` writes `openapi.json` successfully

## Known limitations

### Build-time document is empty
The build-time `openapi.json` is a valid OpenAPI 3.1 skeleton but contains `paths: {}` — the `Microsoft.Extensions.ApiDescription.Server` build target's `dotnet-getdocument` tool does not enumerate minimal API routes in this project. Reproduction in a clean `dotnet new web` test project shows the tool works (4/4 routes found), so the issue is specific to the Hercules host (large service graph, side-effecting startup, mixed minimal-API + `[ApiController]` controllers that aren't registered via `AddControllers()`).

This blocks the acceptance criterion "Документ содержит все ~195 endpoints". The runtime document at `/openapi/v1.json` would be populated (route table is correct, `ShouldInclude` opts every endpoint in), but the runtime cannot be smoke-tested in this environment because of a **pre-existing** `CodeExecutionTool` constructor ambiguity (out of scope for this task — tracked separately).

**Workarounds investigated:**
- `OpenApiOptions.ShouldInclude = _ => true` — applied, doesn't help (tool uses MVC discovery, not the OpenAPI service)
- `AddEndpointsApiExplorer()` — applied, doesn't help (same)
- Reordering `MapOpenApi()` after all `MapXxx()` — doesn't help
- `.WithOpenApi()` on individual routes — doesn't help (tool uses MVC discovery, ignores this)

**Follow-up (next tick or new task):** Either (a) register the `[ApiController]`-based controllers via `AddControllers() + MapControllers()` and debug why MVC discovery still misses minimal API routes, or (b) replace `Microsoft.Extensions.ApiDescription.Server` with a custom MSBuild target that invokes `dotnet run -- --getdocument` and our program writes the document via `IOpenApiDocumentProvider`.

### Pre-existing `CodeExecutionTool` constructor ambiguity
`Hercules.Tools.CodeExecutionTool` has two constructors (`IEnumerable<ICodeExecutor>` and `ICodeExecutor`). With two `ICodeExecutor` registrations (DotnetFileBasedExecutor + SkillSdkExecutor) DI cannot disambiguate, so `app.Build()` throws on validation. This is unrelated to task_109 — it predates this change. Workaround at build-time: skip the registration. Workaround at runtime (out of scope for this task): use a factory `sp => new CodeExecutionTool(sp.GetServices<ICodeExecutor>())`.

### `Microsoft.OpenApi 2.0.0` vulnerability warning
`Microsoft.Extensions.ApiDescription.Server 10.0.0` brings in `Microsoft.OpenApi 2.0.0` (NU1903 — known high severity, GHSA-v5pm-xwqc-g5wc). The newer `Microsoft.OpenApi 2.x` from `Microsoft.AspNetCore.OpenApi 10.0.10+` should fix this; pinned to `10.0.0` for now because the .NET 10 SDK 10.0.400 ships with ApiDescription.Server 10.0.0. Tracked as a dependency upgrade follow-up.

## Implementation notes
- `OpenApiOptions.ShouldInclude` lives in `Microsoft.AspNetCore.OpenApi` 10.0.0+ — it's a `Func<Endpoint, bool>?` predicate that opts endpoints into the default document. With `_ => true`, all registered endpoints are included without per-route `.WithOpenApi()` calls.
- `AddEndpointsApiExplorer()` is a no-op for the runtime OpenAPI service (which has its own minimal-API descriptor provider) but is required for the build-time `dotnet-getdocument` tool's MVC-based discovery.
- `MapOpenApi()` and `MapScalarApiReference()` must be called AFTER all `app.MapXxx()` calls so the route table is finalised before the document provider snapshots it.
- The `isBuildTime` check matches `Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"` — the package's build target sets this when it boots a special host to extract the document.

## Validation
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → **0 errors, 1 expected warning** (`Microsoft.OpenApi 2.0.0` vulnerability, transitive)
- Build target runs and writes `src/agent/Hercules.WebApi/openapi.json` (skeleton — see Known limitations)
- `dotnet test` not run in this tick (pre-existing constructor ambiguity blocks the runtime; the build-time path doesn't exercise the test suite)

## Dependencies
- нет (стартовая задача для OpenAPI pipeline)

## Scope / Likely files
- `src/agent/Hercules.WebApi/Program.cs` — AddOpenApi, MapOpenApi, MapScalarApiReference, isBuildTime, AddEndpointsApiExplorer, build-time guards
- `src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — packages, properties, OpenApiGenerateDocuments
- New: `src/agent/Hercules.WebApi/openapi.json` — generated at build time, committed to git (skeleton; population blocked — see Known limitations)

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