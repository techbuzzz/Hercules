# Hercules Studio — Architecture

> Связанные документы: [SYSTEM-DESIGN.md](SYSTEM-DESIGN.md), [ROADMAP.md](ROADMAP.md)

## 1. Process model

```
┌─────────────────────────────────────────────────────────┐
│                  OS Process: Hercules Studio              │
│                                                           │
│  Main Process (Node.js)                                   │
│  ┌─────────────────────────────────────────────────────┐ │
│  │ app, BrowserWindow, IPC, child_process,             │ │
│  │ safeStorage, Notification, Tray, net (scanner)      │ │
│  │ better-sqlite3 (native addon)                       │ │
│  └─────────────────────────────────────────────────────┘ │
│         │ contextBridge (preload.ts)                      │
│         ▼                                                 │
│  Renderer Process (Chromium, sandboxed)                   │
│  ┌─────────────────────────────────────────────────────┐ │
│  │ Vue 3 app, Monaco, xterm.js, Vue Flow               │ │
│  │ window.studioAPI = IpcApi (from preload)            │ │
│  │ fetch() to agent HTTP API (via main process proxy   │ │
│  │  or directly from renderer with CORS)               │ │
│  └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

**Security flags:**
```typescript
new BrowserWindow({
  webPreferences: {
    preload: path.join(__dirname, 'preload.js'),
    contextIsolation: true,
    nodeIntegration: false,
    sandbox: true,
  }
});
```

## 2. IPC architecture

```
Renderer                      Preload                       Main
┌──────────┐  call          ┌──────────────┐  invoke       ┌──────────────┐
│ Vue      │ ──────────────▶│ contextBridge│ ─────────────▶│ ipcMain      │
│ component│                │ exposeInMain │                │ .handle()    │
│          │◀──────────────│ World('studio│◀──────────────│              │
│          │  return        │ API', {...}) │  result        │              │
└──────────┘                └──────────────┘                └──────────────┘
```

**Typed contract** (`shared/protocol.ts`):
- Renderer видит только `window.studioAPI: IpcApi`
- Preload exposes typed methods через `contextBridge`
- Main handles через `ipcMain.handle()`
- Никаких `any` — все типы в `shared/protocol.ts`

## 3. HTTP request flow

```
Vue component
  → Pinia store (e.g. chatStore)
    → HerculesClient (SDK, renderer)
      → fetch(baseUrl + endpoint, {headers: {X-Api-Key: ...}})
        → Agent (HTTP REST, port 8421)
```

**CORS:** Agent CORS `AllowedCorsOrigins` = `[]` (allow any) в dev. В production Studio renderer грузится из `file://` — CORS не применяется для `file://` origin. Для remote agents (HTTP) — main process может проксировать если CORS проблема.

**Альтернатива:** main process proxy — renderer вызывает `window.studioAPI.http.fetch(url, opts)`, main делает `fetch` из Node (нет CORS). Реализовать если понадобится.

## 4. Agent discovery flow

```
Main Process: AgentScanner
  │
  ├─ scanPorts(8421..8521 + 5000)
  │    ├─ Promise.all(chunks of 50)
  │    │    └─ for each port:
  │    │         TCP connect (300ms timeout)
  │    │         if open: HTTP GET /agent.manifest.json
  │    │           if 200: parse manifest, validate agentId
  │    │           if 401: mark "auth required"
  │    │           else: skip
  │    └─ collect results
  │
  ├─ scanProcesses("Hercules.WebApi.exe", "dotnet.exe")
  │    └─ for each process: query listening ports (via netstat/Get-NetTCPConnection)
  │         probe manifest on found ports
  │
  └─ merge results → SQLite scan_cache → IPC → renderer
```

## 5. Connection lifecycle

```
                    ┌─────────┐
                    │ pending │
                    └────┬────┘
                         │ add connection (URL + key)
                         ▼
                    ┌─────────┐
              ┌─────│connecting│─────┐
              │     └────┬────┘     │
              │ fail    │ success   │
              │         ▼          │
              │    ┌─────────┐    │
              │    │checking │    │
              │    │ health  │    │
              │    └────┬────┘    │
              │         │ ok      │
              │         ▼         │
              │    ┌─────────┐    │
              │    │ checkin │    │
              │    └────┬────┘    │
              │         │ ok      │
              │         ▼         │
              │    ┌─────────┐    │
              │    │checkedIn│◀───┤ reconnect
              │    │ (online)│    │ (exp backoff)
              │    └────┬────┘    │
              │         │ heartbeat 60s
              │         ▼         │
              │    ┌─────────┐    │
              │    │heartbeat│    │
              │    └────┬────┘    │
              │         │ TTL expired / manual / error
              │         ▼         │
              │    ┌─────────┐    │
              └───▶│discon-  │────┘
                   │nected  │
                   └────┬────┘
                        │ checkout / remove
                        ▼
                   ┌─────────┐
                   │ removed │
                   └─────────┘
```

## 6. Auth flow

```
┌────────────┐     contribute key      ┌────────────┐
│  Studio    │ ──────────────────────▶ │   Agent    │
│  (renderer)│ ◀────────────────────── │  ApiKey    │
└────────────┘    response + role       │  Middleware│
                                          └──────┬─────┘
                                                 │ role check
                          ┌──────────────────────┼──────────────────────┐
                          │                      │                      │
                          ▼                      ▼                      ▼
                   ┌───────────┐         ┌───────────┐         ┌───────────┐
                   │contribute │         │  system   │         │  no key   │
                   │ endpoints │         │ endpoints │         │  (401)    │
                   └───────────┘         └───────────┘         └───────────┘

System operation (restart, config PUT, MCP add):
  Studio → dialog "Enter system key"
    → safeStorage.setSystemKey(connectionId, key)
    → HerculesClient.useSystemKey() for next request
    → indicator "system mode" in StatusBar
```

## 7. Skill editor data flow

```
Agent (data/Skills/skill.{id}/)
  ├─ skill.meta.json
  ├─ skill.prompt.md
  ├─ skill.description.md
  ├─ skill.{id}.v{N}.md (history)
  └─ code.cs (if file-based app)

Studio:
  GET /api/skills/{id} → SkillDetailDto
    → Pinia.skills.active
      → SkillEditor.vue
        ├─ Tab: Form (meta fields, chips list for triggers)
        ├─ Tab: Prompt (Monaco, Markdown)
        ├─ Tab: Description (Monaco, Markdown)
        ├─ Tab: C# files (Monaco, C# syntax, if file-based)
        └─ Tab: Raw JSON (Monaco, JSON)

  Edit → SQLite.skill_drafts.save() (autosave)
  "Push to agent":
    → pre-check: POST /api/skills/{id}/manifest/validate
      → if C#: DangerousCodeScanner warnings → show → confirm
    → POST /api/skills (new) or PUT /api/skills/{id} (update)
    → success → SQLite.skill_drafts.markPushed()
  "Test in chat":
    → save draft → Chat view with skill context
  "Diff versions":
    → Monaco diff editor: skill.{id}.v{N}.md vs current
```

## 8. Mesh explorer data flow

```
Agent (GET /api/mesh/agents, /api/mesh/dashboard)
  → Pinia.mesh
    → MeshExplorer.vue (Vue Flow)
      ├─ Nodes: agents (color by health, size by capability count)
      ├─ Edges: delegations/trust relations
      ├─ Click node → sidebar details (capabilities, skills, trust, latency, cost, health, circuit)
      ├─ Context menu: register peer, remove, touch, cleanup, circuit reset
      ├─ Auto-refresh 30s + manual button
      └─ Router explorer: search by capability → ranked candidates table
```

## 9. Consensus flow

```
User selects N agents
  → enters prompt
  → Studio: Promise.all(agents.map(a => POST /api/chat {message: prompt}))
  → responses in columns (one per agent)
  → aggregation:
    ├─ LLM-judge: select one agent → POST /api/chat with all responses + "select best"
    │   → result displayed as "Consensus: {judge agent}'s pick"
    └─ Manual pick: user clicks "Best" on a column
  → (future: Voting, Merge)
```

## 10. Workflow execution — MVP (Stage 8a, Studio-orchestrator)

```
WorkflowDesigner.vue (Vue Flow)
  → graph: nodes (Start, ServiceTask, Conditional, UserTask, End) + edges
  → save to SQLite.workflows

Run:
  Studio executor (renderer or main process)
    ├─ current = Start node
    ├─ loop:
    │    ├─ ServiceTask: POST /api/mesh/intent to target agent
    │    │   → wait response → store result
    │    ├─ Conditional: evaluate expression → next branch
    │    ├─ UserTask: show form (AwaitingInputContext)
    │    │   → user submits → continue
    │    ├─ End: done
    │    └─ update node states in UI (running/done/failed/waiting)
    └─ long-poll /api/mesh/tasks/{id}/poll for async results
```

## 11. Workflow execution — Production (Stage 8b, hercules-workflow-server)

```
hercules-workflow-server (.NET, separate process)
  ├─ REST API: /api/workflows/* (clientId/clientSecret auth)
  │    ├─ POST /api/workflows (save definition)
  │    ├─ POST /api/workflows/{id}/run (start execution)
  │    ├─ GET /api/workflows/{id}/executions (monitor)
  │    └─ GET /api/workflows/executions/{eid} (status)
  ├─ Executor:
  │    ├─ reads graph
  │    ├─ for ServiceTask: POST /api/mesh/intent to agent (workflow→agent auth)
  │    ├─ persists state (SQLite/Postgres)
  │    ├─ handles retries, schedules
  │    └─ triggers: webhook (HTTP POST), cron (Quartz/Hangfire)
  └─ DelegatedTask: linked to workflow execution

Studio:
  ├─ WorkflowDesigner → save to workflow-server
  ├─ "Run" → POST /api/workflows/{id}/run
  └─ Monitor: GET .../executions (long-poll → SSE later)
      → live state per node (running/done/failed/waiting)
      → UserTask: notification + form in Studio → submit → continue
```

## 12. Theme architecture

```
Tailwind CSS v4 + CSS variables

global.css:
  :root { --bg, --fg, --border, --accent, ... }  /* light */
  .dark { --bg, --fg, --border, --accent, ... }  /* dark */

Pinia.settings.theme = "dark" | "light"
  → document.documentElement.classList.toggle("dark")
  → persisted in userData/settings.json
```

## 13. i18n architecture

```
vue-i18n 9+
  ├─ renderer/src/i18n/en.json
  ├─ renderer/src/i18n/ru.json
  └── Pinia.settings.locale = "en" | "ru"
      → persisted in userData/settings.json
      → vue-i18n.global.locale.value = locale
```

## 14. Build pipeline

```
Dev:
  electron-vite dev
    ├─ Vite dev server (renderer, HMR)
    ├─ Main process rebuild (watch)
    └─ Preload rebuild (watch)
  → Electron launches with dev URL

Production:
  electron-vite build
    ├─ Main: tsc → bundle → dist/main/
    ├─ Preload: tsc → bundle → dist/preload/
    └─ Renderer: Vite build → dist/renderer/

Packaging:
  electron-builder (NSIS + portable)
    ├─ dist/ → Hercules-Studio-Setup-0.1.0.exe (NSIS)
    └─ dist/ → Hercules-Studio-0.1.0-portable.zip
```

## 15. CI/CD

```yaml
# .github/workflows/studio-ci.yml
on: push (paths: src/hercules-studio/**)
jobs:
  build-test-package:
    runs-on: windows-latest
    steps:
      - checkout
      - setup-node 22
      - npm install (in src/hercules-studio)
      - biome check
      - vitest run
      - electron-vite build
      - electron-builder (NSIS + portable)
      - upload artifacts
```