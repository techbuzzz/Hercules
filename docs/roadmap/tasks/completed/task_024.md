# Task 24 — Реестр инструментов

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `tool-registry`

## Goal
Навыки объявляют HTTP, FS, shell, DB, GPIO/MQTT и MCP-инструменты из data/Tools/; реестр хранит allow/deny, схемы, лимиты и health state.

## Acceptance criteria

### Sub-tasks

- [x] `Config/AppConfig.cs` — добавить `ToolRegistryConfig` секцию: `Enabled` (default true), `ToolsDir` (default "data/Tools"), `AllowedPatterns` (list, default ["*"]), `DeniedPatterns` (list, default []), `HealthCheckIntervalSeconds` (default 60)
- [x] `Tools/Registry/Models.cs` — `ToolCategory` enum (Http, FileSystem, Shell, Database, Gpio, Mqtt, Mcp, CodeExecution, Internal), `ToolHealthStatus` enum (Unknown, Healthy, Unhealthy, Disabled), `ToolHealthState` record (Status, LastCheckedAt, LastError, ConsecutiveFailures), `ToolRegistryEntry` record (Name, Category, Description, ToolDescriptor, HealthState, Enabled, ConfiguredLimits)
- [x] `Tools/Registry/IToolRegistryService.cs` — интерфейс: `GetEntry(name)`, `GetAllEntries()`, `GetByCategory(cat)`, `GetAllowedTools()`, `IsAllowed(name)`, `UpdateHealthState(name, state)`, `SetEnabled(name, enabled)`, `Reload()`
- [x] `Tools/Registry/ToolRegistryService.cs` — реализация: in-memory ConcurrentDictionary для entries; конструктор принимает `IEnumerable<ITool>`, AppConfig.ToolRegistry, ToolPolicyEngine; apply allow/deny patterns; health state tracking; enabled/disabled per tool
- [x] `Tools/Registry/ToolHealthService.cs` — `IHealthCheck` интерфейс + реализация: async health-check per tool (ping/check method), configurable interval, tracks consecutive failures; fires event on status change
- [x] `Tools/Registry/ToolDiscovery.cs` — file-based discovery из `data/Tools/*.tool.json`: загружает tool metadata, descriptors, limits; fallback к statically registered tools; logs discovered tools at startup
- [x] `Tools/RegistryController.cs` — WebAPI endpoints: `GET /api/tools` (all tools with health), `GET /api/tools/{name}` (single tool), `GET /api/tools/{name}/health` (health status), `POST /api/tools/{name}/enable`, `POST /api/tools/{name}/disable`, `GET /api/tools/categories` (tools by category)
- [x] `Program.cs` (CLI + WebAPI) — зарегистрировать `ToolRegistryConfig`, `ToolHealthService`, `IToolRegistryService` в DI; вызов `ToolDiscovery.Discover()` при старте
- [x] Unit-тесты: `ToolRegistryServiceTests.cs` — 14 тестов: allow/deny patterns, health state updates, enable/disable, category filtering, GetAllowedTools, IsAllowed
- [x] Unit-тесты: `ToolHealthServiceTests.cs` — 8 тестов: health check, status transitions, consecutive failures, interval
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (738/745; 7 pre-existing failures in OtelService, BudgetGuard, WasmTool)

## Scope / Likely files
src/agent/Tools/Registry/, src/agent/Tools/Source/

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Config/AppConfig.cs`** — `ToolRegistryConfig`:
- `Enabled` (default true), `ToolsDir` (default "data/Tools")
- `AllowedPatterns` (default ["*"]), `DeniedPatterns` (default [])
- `HealthCheckIntervalSeconds` (60), `ConsecutiveFailureThreshold` (3)

**`Tools/Registry/Models.cs`** — 5 типов:
- `ToolCategory` enum (Http, FileSystem, Shell, Database, Gpio, Mqtt, Mcp, CodeExecution, Internal, Unknown)
- `ToolHealthStatus` enum (Unknown, Healthy, Unhealthy, Disabled)
- `ToolHealthState` record (Status, LastCheckedAt, LastError, ConsecutiveFailures)
- `ToolLimits` class (MaxCallsPerMinute, TimeoutSeconds, MaxRetries, MaxConcurrent)
- `ToolRegistryEntry` class (Name, Category, Description, Descriptor, HealthState, Enabled, Limits, RegisteredAt, Source, SupportsHealthCheck)

**`Tools/Registry/IToolRegistryService.cs`** — интерфейс:
- `GetEntry`, `GetAllEntries`, `GetByCategory`, `GetAllowedTools`, `IsAllowed`
- `UpdateHealthState`, `SetEnabled`, `Reload`, `RegisterEntry`

**`Tools/Registry/ToolRegistryService.cs`** — реализация:
- `ConcurrentDictionary<string, ToolRegistryEntry>` для entries
- allow/deny glob patterns (`*`, `**`, `?` → regex)
- категоризация tool по имени и policy descriptor
- `IHealthCheckableTool` marker interface для tools с health check
- graceful no-op при registry disabled

**`Tools/Registry/ToolHealthService.cs`** — `BackgroundService`:
- Periodic health check по `HealthCheckIntervalSeconds`
- `RecordFailure` / `RecordSuccess` — tracking consecutive failures
- `CheckAllToolsAsync` / `CheckToolAsync` — public methods для ручного вызова
- `ConsecutiveFailureThreshold` → Unhealthy status

**`Tools/Registry/ToolDiscovery.cs`** — file-based discovery:
- Scan `data/Tools/*.tool.json` при старте
- `ToolDeclarationFile` → `ToolRegistryEntry` conversion
- Merge с policy engine descriptor
- Graceful skip при disabled registry

**`Tools/RegistryController.cs`** — 6 endpoints:
- `GET /api/tools` — все tools с metadata + health
- `GET /api/tools/{name}` — один tool
- `GET /api/tools/{name}/health` — health status
- `GET /api/tools/categories` — tools по категориям
- `POST /api/tools/{name}/enable` — включить
- `POST /api/tools/{name}/disable` — выключить

**DI (CLI + WebAPI):**
- `ToolRegistryConfig` singleton
- `IToolRegistryService` → `ToolRegistryService`
- `ToolHealthService` as `HostedService`
- `ToolDiscovery.Discover()` вызывается при старте

**Tests** — 29 новых тестов:
- `ToolRegistryServiceTests.cs` — 19 тестов: allow/deny patterns, health state updates, enable/disable, category filtering, merge, disabled registry
- `ToolHealthServiceTests.cs` — 10 тестов: RecordFailure/RecordSuccess, status transitions, consecutive failures, threshold

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet test` — 738/745 passed, 7 pre-existing failures (OtelService, BudgetGuard, WasmTool — unrelated to task_024)

## Risks / Rollback
Безопасность динамической загрузки; подписанные sources.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
