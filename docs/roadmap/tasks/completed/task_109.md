# Task 109 — OpenAPI producer + Scalar UI + Build-Time Generation

**Phase:** 8
**Status:** done
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
- `Microsoft.AspNetCore.OpenApi` — NuGet-ссылка (не только встроен в .NET 10 SDK)
- `Scalar.AspNetCore` — отдельный NuGet для Scalar UI
- ~~`Microsoft.Extensions.ApiDescription.Server`~~ — **удалён**: его `dotnet-getdocument`
  не умеет обходить minimal-API-поверхность этого проекта. Build-time генерация выполняется
  собственным MSBuild-таргетом, запускающим приложение с флагом `openapi-output`.
- OpenAPI 3.1 (built-in), не 3.0 (Swashbuckle)
- Без `Produces<T>()` (task_111) response schemas будут пустыми/`object` — это нормально для старта, task_111 добавит типы
- `openapi.json` в корне проекта — не в `bin/`, не в `.gitignore`
- Удаление `ApiDescription.Server` заодно сняло предупреждение NU1903: `Microsoft.OpenApi`
  обновился 2.0.0 → 2.7.5
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
- [x] **Removed** `Microsoft.Extensions.ApiDescription.Server` — its `dotnet-getdocument`
      tool resolves the document through MVC action descriptors and cannot enumerate this
      project's minimal-API surface (writes `paths: {}`); see [Root cause and fix](#root-cause-and-fix-verified)
- [x] Custom MSBuild target `GenerateOpenApiDocument` (`AfterTargets="Build"`) launches the
      app with the `openapi-output` flag
- [x] `<OpenApiDocumentFile>$(MSBuildProjectDirectory)/openapi.json</OpenApiDocumentFile>` в `.csproj`
- [x] `dotnet build` генерирует `openapi.json` в корне `Hercules.WebApi/` проекта
- [x] Build-time detection в Program.cs: флаг `openapi-output` **или** legacy
      `Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"`
- [x] `await app.StartAsync()` перед генерацией — иначе `EndpointDataSource` не финализирован
      и документ сериализуется пустым
- [x] `GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")` — провайдер зарегистрирован
      как keyed-сервис, unkeyed-резолв бросает исключение
- [x] Сериализация `SerializeAsJsonAsync(OpenApi3_1)`, запись UTF-8 **без BOM**
- [x] В build-режиме хотят на эфемерный порт `http://127.0.0.1:0` — сборка не падает,
      если 8421 занят dev-сервером
- [x] Генерация детерминирована: SHA-256 совпадает между двумя `--no-incremental` сборками
- [x] Условное исключение сервисов требующих config при build-time:
  - `CodeExecutionTool` registration
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
- [x] `dotnet build` writes `openapi.json` (custom `GenerateOpenApiDocument` target runs successfully)
- [x] **`openapi.json` содержит 210 paths / 61 schemas** — identical to the runtime document
- [x] Manual smoke: `curl http://localhost:8421/openapi/v1.json` → **200 OK**, 210 paths, 61 schemas
- [x] Manual smoke: Scalar UI на `http://localhost:8421/scalar` → **200 OK**
- [ ] JSON валиден: Spectral (task_117) — deferred, document is now populated so task_117 can run

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

## Root cause and fix (verified)

The empty build-time document was **not** an MVC-vs-minimal-API discovery problem in the
narrow sense, and it was **not** caused by the `CodeExecutionTool` DI ambiguity that the
previous revision of this file claimed. Both of those diagnoses were wrong; re-verified
against the running agent below.

### What was actually wrong

`Microsoft.Extensions.ApiDescription.Server`'s `dotnet-getdocument` tool boots the app with
the `GetDocument.Insider` entry assembly and asks the host to emit its registered documents.
Running that tool by hand against the real assembly printed:

```
Generating document named 'v1'.
Using discovered `GenerateAsync` overload with version parameter.
"No action descriptors found. This may indicate an incorrectly configured application..."
Writing document named 'v1' to ...\openapi.json.
```

and produced a 117-byte skeleton with `paths: {}`. The same tool against a clean
`dotnet new web` probe project failed outright with
`Unable to find service type 'Microsoft.Extensions.ApiDescriptions.IDocumentProvider'`,
i.e. the tool's discovery is not reliable for this host at all.

### The fix

Dropped `Microsoft.Extensions.ApiDescription.Server` and generate the document from the app
itself via a custom `GenerateOpenApiDocument` MSBuild target that launches
`Hercules.WebApi.dll` with the `openapi-output` flag. `Program.cs` then:

1. `await app.StartAsync()` — required. Without a started host the `EndpointDataSource` is
   not finalised and the document serialises with `paths: {}` even though every `MapXxx()`
   call has already run. This was the single decisive step.
2. Resolves the document provider. `AddOpenApi()` registers it as a **keyed** service under
   the key `"v1"`, so unkeyed `GetRequiredService<IOpenApiDocumentProvider>()` throws
   `No service for type ... has been registered`. `GetRequiredKeyedService<...>("v1")` is
   the correct call.
3. Serialises via `document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1)` and writes
   UTF-8 **without BOM** (Orval and Spectral both fail to parse a BOM).

Because this goes through the very same `IOpenApiDocumentProvider` that backs the runtime
`/openapi/v1.json` endpoint, the committed file and the served document cannot drift.

In build mode the host binds an ephemeral loopback port (`http://127.0.0.1:0`) instead of
8421: `StartAsync()` opens a real listener, and a build must not fail because a dev server
already holds 8421, nor expose the production port while doing so. `app.Run()` stays behind
`if (!isBuildTime)` so no path can block on a listening socket.

### `CodeExecutionTool` DI ambiguity — resolved, not pre-existing

The previous revision of this file claimed `app.Build()` threw on constructor ambiguity
because `CodeExecutionTool` had two constructors. That is stale: the type now has a single
`IServiceProvider` constructor (`src/agent/Tools/CodeExecutionTool.cs:24`) and is registered
through an explicit factory (`Program.cs:426`). The agent starts, serves the runtime
document, and serves Scalar. No workaround is needed for it.

### `Microsoft.OpenApi 2.0.0` vulnerability — resolved

`Microsoft.Extensions.ApiDescription.Server 10.0.0` pulled in `Microsoft.OpenApi 2.0.0`
(NU1903, GHSA-v5pm-xwqc-g5wc). Removing that package drops the warning; the project now
resolves `Microsoft.OpenApi 2.7.5` via `Microsoft.AspNetCore.OpenApi 10.0.11`.

## Implementation notes
- `OpenApiOptions.ShouldInclude` is a `Func<Endpoint, bool>?` predicate that opts endpoints
  into the default document. With `_ => true`, all registered endpoints are included without
  per-route `.WithOpenApi()` calls.
- `AddEndpointsApiExplorer()` is a no-op for the runtime OpenAPI service and was only ever
  needed by the removed build tool. It is retained (harmless) but no longer load-bearing.
- `MapOpenApi()` and `MapScalarApiReference()` must be called after all `app.MapXxx()` calls
  so the route table is complete before the document provider snapshots it.
- The `isBuildTime` check accepts two triggers: the legacy `GetDocument.Insider` entry
  assembly (kept for compatibility, no longer wired by the csproj) and the presence of the
  `openapi-output` argument.

## Validation (this revision)
- `dotnet build src/agent/Hercules.slnx` → 0 errors
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → 0 errors, and the
  `GenerateOpenApiDocument` target runs automatically
- Committed `openapi.json`: 145 081 bytes, **210 paths / 61 schemas**, OpenAPI 3.1.1
- Runtime `GET /openapi/v1.json` → 200, 144 788 bytes, **210 paths / 61 schemas** — matches
- Runtime `GET /scalar` → 200

## Dependencies
- нет (стартовая задача для OpenAPI pipeline)

## Scope / Likely files
- `src/agent/Hercules.WebApi/Program.cs` — AddOpenApi, MapOpenApi, MapScalarApiReference, isBuildTime, AddEndpointsApiExplorer, build-time guards, `ReadOpenApiOutputPath`, document emission
- `src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — packages, `OpenApiDocumentFile`, `GenerateOpenApiDocument` target
- `src/agent/Hercules.WebApi/openapi.json` — generated at build time, committed to git (210 paths / 61 schemas)

## Reference
- Article: https://dev.to/nausaf/openapi-in-net-from-setup-to-build-time-generation-with-scalar-ui-1bio
- MS Docs AddOpenApi: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi
- MS Docs build-time: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi#customize-runtime-behavior-during-build-time-document-generation
- Scalar: https://scalar.com/
- MS Docs metadata: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata

## Links
- Backlog: [../../backlog.md](../../backlog.md)
- Epic Studio: [../../../EPIC_Hercules_Studio/README.md](../../../EPIC_Hercules_Studio/README.md)
- task_110 (WithTags): [../task_110.md](../task_110.md)
- task_111 (Produces+DTO): [../task_111.md](../task_111.md)
- task_112 (WithName): [../task_112.md](../task_112.md)
- task_113 (Studio codegen): [../task_113.md](../task_113.md)
- task_117 (Spectral lint): [../task_117.md](../task_117.md)