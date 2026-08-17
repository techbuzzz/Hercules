# Task 25 — MCP-адаптер

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `mcp-adapter`

## Goal
Hercules может потреблять выбранные MCP-серверы и экспонировать подходящие инструменты через MCP-совместимый адаптер. Built-in tools остаются .NET-имплементациями без отдельного процесса.

## Acceptance criteria

### Sub-tasks

- [x] `McpToolAdapter.cs` — wraps `McpClientTool` as `ITool`: name=`mcp.{serverName}.{toolName}`, calls `McpClient.CallToolAsync(tool.Name, args)` via captured client reference, converts result to `ToolResult`
- [x] `McpClientService.cs` — manages multiple MCP server connections: connects via `McpClient.CreateAsync` (StdioClientTransport or HttpClientTransport), tracks `IReadOnlyList<McpClient>` per server, exposes `GetServers()` and `GetServerHealth(serverName)`
- [x] `McpServerHost.cs` — `BackgroundService`: runs in-process MCP server via `AddMcpServer().WithStdioServerTransport()` + `SingleSessionMcpServerHostedService`; wraps each `ITool` as `McpServerTool`; graceful shutdown on `StopAsync`
- [x] `HerculesMcpServerTool.cs` — adapter: `McpServerTool` from `ITool` description + `ParametersSchema` → JSON schema for MCP protocol
- [x] `McpController.cs` — WebAPI: `GET /api/mcp/servers` (name, transport, status, toolCount), `GET /api/mcp/servers/{name}` (server info + tools), `POST /api/mcp/servers/reload` (reload from config)
- [x] `Program.cs` (CLI + WebAPI) — register `McpClientService` as singleton; conditionally start `McpServerHost` when `McpConfig.Server.Enabled=true`; call `McpClientService.InitializeAsync()` at startup
- [x] `Config/AppConfig.cs` — extend `McpConfig`: add `McpServerConfig.Enabled` (default true), `McpServerConfig.HealthCheckEnabled` (default true), `McpServerConfig.TimeoutSeconds` (default 30)
- [x] Unit-тесты: `McpToolAdapterTests.cs` — 16 тестов: tool name format, arguments passthrough, success/error conversion, null result handling, constructor guards
- [x] Unit-тесты: `McpClientServiceTests.cs` — 8 тестов: connect, register tools, health tracking, graceful degradation on server unavailable, reload, dispose
- [x] `dotnet build` проходит без warnings (NU1902 pre-existing OTel advisory)
- [x] `dotnet test` проходит (764/771; 7 pre-existing failures in OtelService, BudgetGuard, WasmTool — unrelated to task_025)

## Scope / Likely files
src/agent/Mcp/, src/agent/Tools/McpAdapter/

## Dependencies
- блокирует / опирается на: [task_024 — tool-registry](task_024.md)

## Implementation notes

### 2026-08-12

**Добавлено:**

**`src/agent/Mcp/Models.cs`** — `McpServerHealthStatus` enum + `McpServerState` record: Name, Transport, Status, ConnectedAt, LastErrorAt, LastError, ToolCount, ServerVersion.

**`src/agent/Mcp/IMcpClientTool.cs`** — interface for testability of `McpToolAdapter`:
- `Name`, `Description`, `JsonSchema` properties
- `CallAsync(IReadOnlyDictionary<string, object?>?, CancellationToken)` → `CallToolResult`

**`src/agent/Mcp/McpToolAdapter.cs`** — `ITool` wrapper for MCP client tools:
- Dual constructor: production `(McpClient, McpClientTool, ...)` + testable `(IMcpClientTool, ...)`
- Name = `mcp.{serverName}.{toolName}`, Description passthrough, ParametersSchema extraction
- `ExecuteAsync`: JSON parsing → `CallAsync` → `ExtractText` → `ToolResult.Ok` or `ToolResult.Fail` (when `result.IsError == true`)
- `McpClientToolAdapter` — internal production bridge from `McpClientTool` to `IMcpClientTool`

**`src/agent/Mcp/McpClientService.cs`** — manages MCP server connections:
- `InitializeAsync`: connects via `McpClient.CreateAsync` with StdioClientTransport or HttpClientTransport
- Per-server: lists tools, wraps each with `McpToolAdapter`, registers with `ToolRegistryService`
- `McpServerConnection` — holds client + state, implements `IAsyncDisposable`
- `ReloadAsync`: disconnects all, clears, reinitializes
- Graceful degradation: failed connections recorded as Unhealthy

**`src/agent/Mcp/McpServerHost.cs`** — `BackgroundService` hosting Hercules tools as MCP server:
- Runs via `McpServer.Create(StdioServerTransport(...))` + `McpServerOptions` with `McpServerHandlers`
- `ListToolsHandler`: uses `HerculesMcpServerTool` to build `Tool` list
- `CallToolHandler`: dispatches to `ITool.ExecuteAsync`, wraps as `CallToolResult`
- Graceful shutdown: `StopAsync` + `appLifetime.StopApplication()` on stdio exit

**`src/agent/Mcp/HerculesMcpServerTool.cs`** — adapter converting `ITool` → MCP `Tool`:
- `ToMcpTool()`: builds `Tool` with `Name`, `Description`, `InputSchema` from `ParametersSchema`
- Handles missing/empty schema with default `{"type":"object"}` schema

**`src/agent/Hercules.WebApi/Controllers/McpController.cs`** — 3 endpoints:
- `GET /api/mcp/servers` — all servers with status and tool count
- `GET /api/mcp/servers/{name}` — single server details
- `POST /api/mcp/servers/reload` — reload from config

**`src/agent/Config/AppConfig.cs`** — `McpServerConfig` extended with:
- `Enabled` (default true), `HealthCheckEnabled` (default true), `TimeoutSeconds` (default 30)

**`src/agent/Tools/Registry/ToolRegistryService.cs`** — added `RegisterTool(ITool)`:
- Checks allow/deny patterns, infers category from name, creates `ToolRegistryEntry` with Source="Mcp"

**`Program.cs` (CLI + WebAPI)**:
- Added `using Hercules.Mcp;`
- `McpClientService` registered as singleton
- `McpServerHost` registered as `HostedService`
- `InitializeAsync()` called at startup with error logging

**`tests/.../Mcp/McpToolAdapterTests.cs`** — 16 тестов:
- Name scoping (3), description passthrough (2), ParametersSchema (3), ExecuteAsync success/error/cancel/null (7), constructor guards (2)

**`tests/.../Mcp/McpClientServiceTests.cs`** — 8 тестов:
- No servers, empty name skip, unknown transport → unhealthy, idempotency, reload, dispose, state count

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors (NU1902 pre-existing)
- `dotnet build Hercules.WebApi.csproj` — 0 errors (NU1902 pre-existing)
- `dotnet test` — 764/771 passed (7 pre-existing failures: OtelService × 5, BudgetGuard × 1, WasmTool × 1 — unrelated to task_025)

## Risks / Rollback
Поверхность атаки MCP-серверов; strict capability allowlist.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
