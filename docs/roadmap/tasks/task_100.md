# Task 100 — MCP hot-reload (IConfigReload)

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `mcp-hot-reload`
**Studio Stage:** 6

## Goal
Реализовать `McpClientService : IConfigReload`, чтобы изменения MCP-серверов через `PATCH /api/config` применялись без рестарта процесса. `POST /api/mcp/servers/reload` переустанавливает соединения с обновлённым конфигом.

## Acceptance criteria
- [ ] `McpClientService` implements `IConfigReload`
  - `Reload(AppConfig)` → read new `McpConfig` from config, dispose old connections, reinitialize with new server list
  - Register in `RuntimeConfigReactor` as `IConfigReload` consumer
- [ ] `McpClientService._config` — не singleton snapshot, а читает из `RuntimeConfigStore.Current.Mcp` при reload
- [ ] `POST /api/mcp/servers/reload` → `ReloadAsync()` → uses fresh config
- [ ] `PATCH /api/config` с `mcp.servers` → `RuntimeConfigReactor` triggers `McpClientService.Reload()` → new servers connected, old removed
- [ ] No restart required after MCP config change
- [ ] Unit tests: reload adds new server, removes old, updates existing
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- нет (можно делать независимо, зависит только от текущей архитектуры)

## Scope / Likely files
src/agent/Mcp/McpClientService.cs, src/agent/Hercules.WebApi/Config/RuntimeConfigReactor.cs

## Links
- Studio Stage 5: [../EPIC_Hercules_Studio/tasks/stage_05_tools_mcp.md](../EPIC_Hercules_Studio/tasks/stage_05_tools_mcp.md)
- Backlog: [../backlog.md](../backlog.md)