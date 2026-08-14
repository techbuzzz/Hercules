# Stage 6 — Тонкая настройка + Restart + SkillSdk + Context Distillation + Postgres

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 2-3 недели
**Dependencies (backend):** task_099 (restart), task_100 (MCP reload), task_101 (SkillSdk), task_102 (context distillation), task_103 (Postgres session store)
**Dependencies (Studio):** Stage 2, Stage 5

## Goal

Полная настройка агента (LLM, roles, mesh, quotas, context budget) + удалённый restart (supervisor protocol) + C# file-based apps test-run через sandbox + context distillation UI + Postgres centralized storage config.

## Tasks

### 6.1 — Config view

- [ ] `views/Config.vue` (tabbed):
  - **LLM Providers** — editor for LLM config (providers, models, apiKeys, fallback chains)
  - **Roles** — role routing (main, code_writer, reflector → provider/model mapping)
  - **Mesh/A2A** — endpoints list for static discovery, peer URLs
  - **Quotas** — rate limits, budget limits per scope
  - **Context** — context budget presets + advanced slider, distillation settings
  - **Raw JSON** — Monaco full config editor with diff

### 6.2 — LLM Providers editor

- [ ] `components/config/LlmProvidersEditor.vue`:
  - List of providers: name (OpenAI, Anthropic, OpenRouter, Ollama, etc.), model, apiKey, baseUrl
  - Add/remove provider
  - Fallback chain: drag-and-drop order
  - "Test provider" → `GET /api/llm/health/{provider}` → show status
  - "Test connection" → send test prompt → verify response
  - Save → `PATCH /api/config` (llm section) → hot-reload works
  - Pre-check: validate provider config before save (non-empty apiKey, valid model)

### 6.3 — Roles editor

- [ ] `components/config/RolesEditor.vue`:
  - Roles: main, code_writer, reflector (configurable list)
  - Per role: provider, model, temperature, maxTokens
  - Add/remove custom roles
  - Save → `PATCH /api/config` (roles section) → hot-reload works

### 6.4 — Mesh/A2A endpoints editor

- [ ] `components/config/MeshEndpointsEditor.vue`:
  - Static peers: list of {name, url} → `GET /api/mesh/discovery` shows sources
  - Add/remove peer URL
  - A2A endpoints: {name, url} for agent-to-agent
  - Save → `PATCH /api/config` (mesh.peers, a2a.endpoints)

### 6.5 — Quotas editor

- [ ] `components/config/QuotasEditor.vue`:
  - Per-scope quotas: agent, skill, tool, user
  - Fields: rateLimit (per minute), budgetLimit (USD per day/month)
  - Budget guardrails: hard cap, warning threshold
  - Save → `PATCH /api/config` (quotas section)

### 6.6 — Context budget + distillation

- [ ] `components/config/ContextBudgetEditor.vue`:
  - Presets: Экономный (low token usage), Сбалансированный (medium), Полный (high)
  - Advanced slider: maxTokens, maxMessages, maxToolOutputBytes
  - Distillation settings:
    - Mode: off | auto | manual
    - Strategy: hierarchical (recent=raw, older=summary, ancient=key facts)
    - Summary interval (every N messages)
    - Key facts extraction toggle
  - "Run distillation now" → `POST /api/context/distill`
  - "View summary" → `GET /api/context/summary` → show in Monaco (markdown)
  - "Compress trace" → `POST /api/context/trace/compress`
  - Save → `PATCH /api/config` (context section)

### 6.7 — Raw config editor

- [ ] `components/config/RawConfigEditor.vue`:
  - Monaco JSON editor
  - `GET /api/config` → load current
  - Edit in Monaco
  - "Show diff" → diff current vs edited (Monaco diff editor)
  - "Save (PUT full)" → system key required → `PUT /api/config`
  - "Save (PATCH)" → contribute key → `PATCH /api/config` (JSON merge patch)
  - Validation: JSON syntax check before save

### 6.8 — Restart agent

- [ ] `components/config/RestartAgent.vue`:
  - "Restart agent" button → system key dialog → confirm
  - `POST /api/system/restart` (system key) → agent sets restart flag
  - Poll `GET /api/system/restart-pending` → show "restart pending..."
  - If Studio-managed (future): kill + spawn
  - If external: "restart requested, waiting for supervisor"
  - After restart: reconnect, checkin, health check
  - Warning: "This will temporarily disconnect the agent. Unsaved changes may be lost."
- [ ] `stores/activeAgent.ts` — `restart()` method, `restartPending` state

### 6.9 — MCP hot-reload (после task_100)

- [ ] После доработки бэкенда `McpClientService : IConfigReload`:
  - MCP config change (Stage 5) → `PATCH /api/config` → `POST /api/mcp/servers/reload` → hot-reload
  - Remove warning banner from Stage 5
  - Test: add MCP server → reload → see connected without restart

### 6.10 — C# file-based apps test-run

- [ ] `components/skills/CsharpTestRun.vue`:
  - In SkillEditor "C# files" tab → "Test run" button
  - Two modes:
    - **Local:** Studio main process → `dotnet run --file code.cs` → xterm.js output panel
    - **Sandbox (backend):** `POST /api/...execute...` (sandbox endpoint) → result in panel
  - xterm.js panel in BottomPanel: shows stdout/stderr, exitCode, duration
  - Pre-check: DangerousCodeScanner warnings → confirm before run
  - "Stop" button → kill process (local) or cancel (sandbox)
- [ ] `electron/main/native-bridge.ts` — `spawnProcess(cmd, args, cwd)` → xterm output stream via IPC

### 6.11 — SkillSdk integration (после task_101)

- [ ] In skill templates (Stage 3): show SkillSdk API references
- [ ] C# template with SkillSdk usage examples:
  - `IHttpClient` — REST calls to allowed domains
  - `IMcpClient` — MCP tool calls
  - `ILlmClient` — LLM chat completion
  - `IMemoryClient` — read/write agent memory
  - `ISkillLogger` — structured logging
  - `ISessionContext` — current session info
- [ ] Monaco csharp: SkillSdk type hints (basic, via TypeScript d.ts? No — C# types. Just syntax highlighting for MVP)

### 6.12 — Postgres centralized storage config (после task_103)

- [ ] `components/config/StorageConfig.vue`:
  - Storage mode: SQLite (default) | Postgres (centralized)
  - Postgres connection: host, port, database, user, password, schema
  - "Collective mind mode" toggle: shared memory across agents (opt-in)
  - Session isolation: per-agent (default) | shared
  - "Test connection" → ping Postgres → show status
  - Save → `PATCH /api/config` (storage section) → may require restart
  - Warning if switching SQLite → Postgres: "Data migration required"

### 6.13 — Sidebar: Config activity

- [ ] Sidebar for "Config" activity:
  - Sub-nav: LLM, Roles, Mesh, Quotas, Context, Storage, Raw JSON, Restart
  - Click → opens corresponding editor in MainWorkbench

### 6.14 — Tests

- [ ] Unit: config stores, LLM provider validation, context budget presets
- [ ] E2E: edit LLM provider → save → hot-reload → test chat works, restart agent → reconnect

## Acceptance criteria

- [ ] LLM providers: add/edit/remove, test provider, fallback chain, hot-reload works
- [ ] Roles: edit role→provider/model mapping, hot-reload
- [ ] Mesh endpoints: add/remove peer URLs
- [ ] Quotas: edit rate/budget limits
- [ ] Context: presets + slider, distillation mode, "run now", view summary
- [ ] Raw config: Monaco editor, diff view, PUT (system) / PATCH (contribute)
- [ ] Restart: button → system key → POST restart → poll pending → reconnect
- [ ] MCP hot-reload: config change → reload → no restart needed (после task_100)
- [ ] C# test-run: local (dotnet run) + sandbox (backend), xterm.js output, pre-check scanner
- [ ] SkillSdk: templates show API references, C# examples
- [ ] Postgres: config storage mode, connection test, collective mind toggle
- [ ] `npm run build` + tests pass

## Scope / Likely files

- `renderer/src/views/Config.vue`
- `renderer/src/components/config/` (LlmProvidersEditor, RolesEditor, MeshEndpointsEditor, QuotasEditor, ContextBudgetEditor, RawConfigEditor, RestartAgent, StorageConfig)
- `renderer/src/components/skills/CsharpTestRun.vue`
- `renderer/src/stores/config.ts`

## Dependencies

- **Backend:** task_099 (restart), task_100 (MCP reload), task_101 (SkillSdk), task_102 (distillation), task_103 (Postgres)
- **Studio:** Stage 2, Stage 5

## Risks / Rollback

- **Restart protocol:** agent self-kill is dangerous. Mitigation: supervisor-протокол (flag + poll), Studio doesn't kill external agents.
- **Postgres migration:** switching storage may lose data. Mitigation: warning + migration tool (future).
- **C# local run:** security risk running untrusted code locally. Mitigation: pre-check scanner, confirm dialog, sandbox preferred.

## Links

- Epic: [../README.md](../README.md)
- Stage 2: [stage_02_chat_skills.md](stage_02_chat_skills.md)
- Stage 5: [stage_05_tools_mcp.md](stage_05_tools_mcp.md)
- ADR-0008 (workflow-server): [../adr/0008-workflow-server-architecture.md](../adr/0008-workflow-server-architecture.md)