# Stage 5 — Tool Registry + MCP Management

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 1-2 недели
**Dependencies (backend):** нет (MCP add/remove через PATCH config, warning restart до Stage 6)
**Dependencies (Studio):** Stage 1

## Goal

Управление инструментами (enable/disable/health) и MCP-серверами (list/add/edit/delete/reload) на агенте. Pre-check MCP before push.

## Tasks

### 5.1 — Tools store

- [ ] `stores/tools.ts`:
  - `tools: ToolEntry[]` — from `GET /api/tools`
  - `categories: ToolCategory[]` — from `GET /api/tools/categories`
  - `health: Map<toolName, HealthStatus>`
  - `load()`, `enable(name)`, `disable(name)`, `checkHealth(name)`
  - `reload()` — re-fetch list

### 5.2 — Tools view

- [ ] `views/Tools.vue`:
  - Table: name, category, description, enabled (toggle), health (badge)
  - Filter by category (dropdown from categories)
  - Search by name
  - Enable/disable toggle → `POST /api/tools/{name}/enable` | `POST /api/tools/{name}/disable`
  - Health check per tool → `GET /api/tools/{name}/health` → badge: healthy (green), unhealthy (red), unknown (gray)
  - Click tool row → details panel: parametersSchema, description, category, enabled, health
- [ ] `components/tools/ToolDetailsPanel.vue`:
  - Full tool info
  - Parameters schema (JSON viewer)
  - Health history (if available)
  - Enable/disable button

### 5.3 — MCP store

- [ ] `stores/mcp.ts`:
  - `servers: McpServer[]` — from `GET /api/mcp/servers`
  - `serverDetail: Map<name, McpServerDetail>` — from `GET /api/mcp/servers/{name}`
  - `load()`, `reload()` (POST /api/mcp/servers/reload), `getDetail(name)`
  - `addServer(config)` — PATCH /api/config (mcp.servers)
  - `updateServer(name, config)` — PATCH /api/config (mcp.servers.{name})
  - `deleteServer(name)` — PATCH /api/config (mcp.servers.{name} = null)

### 5.4 — MCP Manager view

- [ ] `views/McpManager.vue`:
  - List MCP servers: name, transport (stdio/http/sse), command/url, status (connected/disconnected), tools count
  - "Add server" button → McpServerForm
  - "Edit" button per server → McpServerForm (pre-filled)
  - "Delete" button per server → confirm → PATCH config
  - "Reload" button → `POST /api/mcp/servers/reload`
  - Server detail: tools list (name, description), health, connection status
  - Warning banner: "MCP server changes require agent restart until Stage 6 (hot-reload)" — shown after config change

### 5.5 — MCP server form

- [ ] `components/mcp/McpServerForm.vue`:
  - Fields:
    - name (unique identifier)
    - transport: stdio | http | sse (radio)
    - **stdio:** command, args (array), env (key-value)
    - **http/sse:** url, headers (key-value)
    - enabled (toggle)
  - Validation: name required, transport-specific fields required
  - Pre-check: "Test connection" button:
    - For http/sse: try HTTP GET to url → check response
    - For stdio: validate command exists (which/where)
    - Show "Connection OK" or error
  - Save → PATCH /api/config (mcp.servers) → warning "restart required" (до Stage 6)
  - After Stage 6: save → MCP hot-reload → no restart needed

### 5.6 — Pre-check MCP

- [ ] Before saving MCP config:
  - Validate JSON structure
  - For http/sse: HTTP probe
  - For stdio: command exists check
  - Show validation results before applying
  - "Apply anyway" if warnings

### 5.7 — Sidebar: Tools activity

- [ ] Sidebar for "Tools" activity:
  - Sub-nav: Tool Registry, MCP Servers
  - Click → opens corresponding view in MainWorkbench

### 5.8 — Tests

- [ ] Unit: tools store enable/disable, MCP store add/edit/delete (mock API)
- [ ] E2E: open tools → toggle enable → see state change, add MCP server → see in list

## Acceptance criteria

- [ ] Tools: table with name/category/enabled/health, toggle enable/disable works
- [ ] Tool health check: badge updates after check
- [ ] Tool details: parameters schema visible
- [ ] MCP servers: list with transport, command/url, status
- [ ] MCP add: form with stdio/http/sse fields, validation, pre-check
- [ ] MCP edit: pre-filled form, save → PATCH config
- [ ] MCP delete: confirm → remove from config
- [ ] MCP reload: button → POST reload → servers reconnect
- [ ] Warning banner: "restart required" after MCP config change (until Stage 6)
- [ ] `npm run build` + tests pass

## Scope / Likely files

- `renderer/src/views/Tools.vue`, `views/McpManager.vue`
- `renderer/src/components/tools/ToolDetailsPanel.vue`
- `renderer/src/components/mcp/McpServerForm.vue`
- `renderer/src/stores/tools.ts`, `stores/mcp.ts`

## Dependencies

- **Backend:** нет (текущий tools/MCP API; MCP add/remove через PATCH config)
- **Studio:** Stage 1

## Risks / Rollback

- **PATCH config for MCP:** `McpClientService` не реализует `IConfigReload` → changes not applied until restart. Warning shown. Fixed in Stage 6 (task_100).
- **Stdio command validation:** Windows `where` vs Unix `which`. Mitigation: use `which` npm package or Node `child_process.execFile`.

## Links

- Epic: [../README.md](../README.md)
- Stage 1: [stage_01_agent_scanner.md](stage_01_agent_scanner.md)
- Stage 6: [stage_06_config_restart.md](stage_06_config_restart.md) (MCP hot-reload)