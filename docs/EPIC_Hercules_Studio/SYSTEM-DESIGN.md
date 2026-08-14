# Hercules Studio — System Design

> Связанные документы: [README.md](README.md), [ARCHITECTURE.md](ARCHITECTURE.md), [ROADMAP.md](ROADMAP.md)

## 1. Контекст и цели

Hercules Studio — десктопное IDE-приложение для управления несколькими агентами Hercules с одного рабочего места. Studio подключается к **уже запущенным** агентам по HTTP REST API, не spawns их сама.

**Главные user stories:**
- Как оператор, я хочу подключиться к нескольким агентам на моей машине и переключаться между ними
- Как разработчик навыков, я хочу редактировать промпты и C# код в Monaco, тестировать локально, пушить на агента
- Как администратор, я хочу тонко настраивать LLM/roles/mesh/quotas агента и видеть изменения в runtime
- Как исследователь, я хочу собрать консилиум агентов и получить агрегированный ответ
- Как архитектор процессов, я хочу визуально спроектировать multi-agent workflow и запустить его

**Non-goals (на MVP):**
- Studio не запускает агентов (подключается к существующим)
- Studio не бандлит .NET runtime
- macOS/Linux сборки (Windows only на старте)
- Полноценный marketplace каталог (push skill на агента — достаточно)
- LSP IntelliSense для C# (базовая подсветка на MVP)

## 2. Высокоуровневая архитектура

```
┌──────────────────────────────────────────────────────────────────────┐
│                         Hercules Studio (Electron)                     │
│                                                                        │
│  ┌────────────────────────── Main Process (Node.js) ───────────────┐  │
│  │                                                                 │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────────────┐ │  │
│  │  │ Window   │  │ Agent    │  │ Native   │  │ SQLite         │ │  │
│  │  │ Manager  │  │ Scanner  │  │ Bridge   │  │ (better-       │ │  │
│  │  │          │  │ (ports + │  │ (fs,     │  │  sqlite3)      │ │  │
│  │  │          │  │ procs)   │  │  shell,  │  │ studio.db      │ │  │
│  │  │          │  │          │  │  keys,   │  │                │ │  │
│  │  │          │  │          │  │  notify, │  │ ┌────────────┐ │ │  │
│  │  │          │  │          │  │  tray)   │  │ │chat_history│ │ │  │
│  │  │          │  │          │  │          │  │ │skill_drafts│ │ │  │
│  │  │          │  │          │  │          │  │ │workflows   │ │ │  │
│  │  │          │  │          │  │          │  │ │scan_cache  │ │ │  │
│  │  └──────────┘  └──────────┘  └──────────┘  │ └────────────┘ │ │  │
│  │                                              └────────────────┘ │  │
│  │  ┌──────────────────────────────────────────────────────────┐ │  │
│  │  │ Connection Manager                                        │ │  │
│  │  │  - source registry (userData/connections.json)           │ │  │
│  │  │  - API keys in safeStorage (OS keychain)                  │ │  │
│  │  │  - health polling (30s) + exponential backoff reconnect  │ │  │
│  │  │  - CheckIn/CheckOut heartbeat (60s TTL)                  │ │  │
│  │  └──────────────────────────────────────────────────────────┘ │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                │                                       │
│                    IPC (contextBridge, preload)                        │
│                                ▼                                       │
│  ┌────────────────────────── Renderer (Vue 3 + Vite) ───────────────┐ │
│  │                                                                   │ │
│  │  ┌──────────┐  ┌──────────┐  ┌────────────────────────────────┐  │ │
│  │  │ Activity │  │ Sidebar  │  │ Main Workbench                  │  │ │
│  │  │ Bar      │  │          │  │                                 │  │ │
│  │  │          │  │ Agent    │  │ ┌─────┐ ┌─────┐ ┌─────┐        │  │ │
│  │  │ Agents   │  │ tree /   │  │ │Chat │ │Skill│ │Mesh │ ...    │  │ │
│  │  │ Chat     │  │ skill    │  │ │View │ │Edit │ │Expl │        │  │ │
│  │  │ Skills   │  │ list /   │  │ └─────┘ └─────┘ └─────┘        │  │ │
│  │  │ Mesh     │  │ mesh     │  │                                 │  │ │
│  │  │ Tools    │  │ nodes /  │  │ ┌─────────────────────────┐    │  │ │
│  │  │ Config   │  │ ...      │  │ │ Bottom Panel             │    │  │ │
│  │  │ Workflow │  │          │  │ │ (xterm.js terminal,      │    │  │ │
│  │  │ Consensus│  │          │  │ │  logs, problems)         │    │  │ │
│  │  └──────────┘  └──────────┘  │ └─────────────────────────┘    │  │ │
│  │                              └────────────────────────────────┘  │ │
│  │  ┌──────────┐  ┌──────────┐  ┌────────────────────────────────┐  │ │
│  │  │ Status   │  │ Command  │  │ Pinia Stores                    │  │ │
│  │  │ Bar      │  │ Palette  │  │ connections, activeAgent,        │  │ │
│  │  │ (agent,  │  │ (Ctrl+   │  │ chat, skills, mesh, tools,       │  │ │
│  │  │  status, │  │  Shift+P)│  │ config, workflow, notifications  │  │ │
│  │  │  latency,│  │          │  │                                 │  │ │
│  │  │  tokens, │  │          │  │ SDK: HerculesClient (TS)        │  │ │
│  │  │  checkin)│  │          │  │ (endpoints, DTOs, auth)         │  │ │
│  │  └──────────┘  └──────────┘  └────────────────────────────────┘  │ │
│  └───────────────────────────────────────────────────────────────────┘
└──────────────────────────────────────────────────────────────────────┘
                                │
                                │ HTTP REST (X-Api-Key, contribute/system)
                                ▼
┌──────────────────────────────────────────────────────────────────────┐
│              Hercules.WebApi (.NET) — Agent instances                  │
│                                                                        │
│  Agent A (port 8421)    Agent B (port 8422)    Agent C (port 8423)    │
│  ┌────────────────┐     ┌────────────────┐     ┌────────────────┐    │
│  │ data/          │     │ data/          │     │ data/          │    │
│  │  Skills/       │     │  Skills/       │     │  Skills/       │    │
│  │  Memory/       │     │  Memory/       │     │  Memory/       │    │
│  │  sessions.db   │     │  sessions.db   │     │  sessions.db   │    │
│  │  manifest.json │     │  manifest.json │     │  manifest.json │    │
│  └────────────────┘     └────────────────┘     └────────────────┘    │
│         │                      │                      │              │
│         └────────── mesh ──────┴──────────────────────┘              │
└──────────────────────────────────────────────────────────────────────┘
                                │
                                │ /api/mesh/intent (workflow-server → agents)
                                ▼
┌──────────────────────────────────────────────────────────────────────┐
│         hercules-workflow-server (.NET) — Stage 8 production          │
│  - workflow definitions (graph models)                                │
│  - executor/interpreter                                               │
│  - triggers: webhook + cron (MVP), file watcher + event bus later     │
│  - clientId/clientSecret auth                                         │
│  - execution history, state                                            │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. Компоненты

### 3.1 Main Process (Node.js / TypeScript)

| Компонент | Ответственность |
|---|---|
| **WindowManager** | Создание BrowserWindow, lifecycle, single-instance lock |
| **AgentScanner** | Port range scan (8421-8521 + legacy 5000), Windows process scan, manifest probe |
| **ConnectionManager** | Persists connections, health polling (30s), reconnect (exp backoff), CheckIn/CheckOut heartbeat (60s TTL) |
| **NativeBridge** | fs (read/write skill files), shell (open external, xterm spawn), safeStorage (API keys), Notification, Tray |
| **SQLiteStore** | better-sqlite3, tables: chat_history, skill_drafts, workflows, scan_cache |
| **LicenseManager** | First-run consent, AGPL/commercial indicator, `userData/license-consent.json` |
| **IpcHandler** | contextBridge API surface, typed IPC contracts (shared/protocol.ts) |

### 3.2 Renderer (Vue 3 + Vite)

| Компонент | Ответственность |
|---|---|
| **ActivityBar** | Вертикальный бар иконок: Agents, Chat, Skills, Mesh, Tools, Config, Workflow, Consensus |
| **Sidebar** | Контекстный контент под активной activity (agent tree, skill list, mesh nodes, etc.) |
| **MainWorkbench** | Табы с views, drag-and-drop splitting (future) |
| **StatusBar** | Активный агент, статус, latency, token usage, check-in status, версия Studio |
| **BottomPanel** | xterm.js terminal (test-run + общий), logs, problems |
| **CommandPalette** | Ctrl+Shift+P, простой вариант на MVP |
| **Pinia stores** | connections, activeAgent, chat, skills, mesh, tools, config, workflow, notifications |
| **HerculesClient (SDK)** | TypeScript клиент к Hercules API: endpoints, DTOs, auth (contribute/system) |

### 3.3 Views

| View | Stage | Описание |
|---|---|---|
| **AgentList** | 1 | Список подключённых агентов, статус, manage, restart |
| **DiscoverAgents** | 1 | Результаты сканирования, add connection |
| **Chat** | 2 | Чат с активным агентом, история, typing effect, markdown |
| **SkillEditor** | 2 | Monaco: skill.prompt.md, skill.meta.json (form + raw), skill.description.md |
| **SkillPush** | 3 | Push skill на агента (POST/PUT/import), cross-agent install |
| **MeshExplorer** | 4 | Vue Flow graph: topology, health, router, shared memory, circuits |
| **Tools** | 5 | Tool registry: list, enable/disable, health |
| **McpManager** | 5 | MCP servers: list, add, edit, delete, reload, pre-check |
| **Config** | 6 | LLM providers, roles, mesh endpoints, quotas, context budget, raw JSON editor |
| **Consensus** | 7 | Multi-agent chat, columns side-by-side, LLM-judge + manual pick |
| **WorkflowDesigner** | 8 | Vue Flow BPMN canvas, palette, run, live monitoring |
| **EmptyState** | 0 | Welcome + marketing carousel + quickstart |

## 4. Потоки данных

### 4.1 Подключение к агенту

```
User → "Add Connection" form (URL + API key)
  → ConnectionManager.validate(url, key)
    → GET /api/health (no auth)
    → GET /agent.manifest.json (X-Api-Key)
    → validate agentId non-empty
  → safeStorage.set(key)
  → connections.json.add({name, baseUrl, agentId, displayName, authScheme})
  → POST /api/system/checkin (X-Api-Key, studioId)
  → Pinia.connections.add()
  → StatusBar update
  → heartbeat timer (60s)
```

### 4.2 Сканирование агентов

```
User → "Scan for agents" (or auto on startup)
  → AgentScanner.scanPorts(8421-8521 + 5000)
    → parallel (50 concurrent, 300ms timeout)
    → for each open port: GET /agent.manifest.json
    → validate manifest (agentId, capabilities)
    → if 401: mark "auth required"
  → AgentScanner.scanProcesses("Hercules.WebApi.exe")
    → for each process: query listening ports
    → probe manifest on found ports
  → SQLite.scan_cache.update()
  → Pinia.discovered.set(results)
  → UI shows found agents, "Add" button per agent
```

### 4.3 Редактирование и push навыка

```
User → SkillEditor (Monaco)
  → edit skill.prompt.md + skill.meta.json + skill.description.md
  → SQLite.skill_drafts.save(local draft)
  → "Test in chat" → Chat view with skill active
  → "Push to agent"
    → select target agent(s)
    → if new: POST /api/skills (contribute key)
    → if exists: PUT /api/skills/{id} (contribute key)
    → if package: POST /api/skills/import (multipart)
    → pre-check: POST /api/skills/{id}/manifest/validate (if C# — DangerousCodeScanner)
    → show validation warnings
    → confirm push
  → success toast + skill appears in agent's list
```

### 4.4 Консилиум

```
User → Consensus view
  → select N agents from connections
  → enter prompt
  → Studio parallel: Promise.all(agents.map(a => POST /api/chat))
  → results in columns side-by-side
  → aggregation:
    → LLM-judge: select one agent as judge, send all responses, get best
    → Manual pick: user selects best response
  → show aggregated result
```

### 4.5 Workflow execution (MVP — Studio-orchestrator)

```
User → WorkflowDesigner (Vue Flow)
  → design graph: Start → ServiceTask(A) → Conditional → ServiceTask(B)/ServiceTask(C) → End
  → save to SQLite.workflows
  → "Run"
    → Studio executor reads graph
    → for each ServiceTask: POST /api/mesh/intent to target agent
    → wait response, evaluate condition
    → next task
    → live update node states in UI
  → monitoring: long-poll /api/mesh/tasks/{id}/poll for AwaitingInput
  → User Task: show form (text/file/approval/choice) → submit → continue
```

### 4.6 Workflow execution (Production — hercules-workflow-server)

```
User → WorkflowDesigner
  → save graph → POST /api/workflows (to workflow-server, clientId/clientSecret auth)
  → "Run" → POST /api/workflows/{id}/run
  → workflow-server executor:
    → reads graph
    → for each ServiceTask: POST /api/mesh/intent to agent (workflow-server→Agent auth)
    → persists state, handles retries, schedules
    → triggers: webhook, cron
  → Studio monitors: GET /api/workflows/{id}/executions (long-poll/SSE)
  → live state per node
```

## 5. Технологический стек

| Слой | Технология | Версия | Почему |
|---|---|---|---|
| Desktop shell | Electron | 30+ | Зрелый, Windows-фокус, Monaco, auto-updater |
| Build (dev) | electron-vite | latest | HMR renderer + main, TS из коробки |
| Build (packaging) | electron-builder | latest | NSIS installer, portable zip |
| Main process | TypeScript | 5.x | Типизация IPC, shared типы |
| Renderer framework | Vue 3 | 3.4+ | Composition API, `<script setup>` |
| Bundler | Vite | 5+ | Быстрый HMR |
| State | Pinia | 2+ | Стандарт Vue, типизированные stores |
| UI components | shadcn-vue | latest | Tailwind-based, копируем нужные |
| Styling | Tailwind CSS | 4+ | Консистентность с hercules-web |
| Icons | Tabler Icons | latest | Есть лицензия |
| Editor | Monaco Editor | latest | Skill/prompt/config editing, diff |
| Terminal | xterm.js | latest | Test-run output, встроенный терминал |
| Graph viz | Vue Flow | 1+ | Mesh topology + BPMN designer |
| Local DB | better-sqlite3 | latest | chat history, drafts, workflows, scan cache |
| Markdown | markdown-it + highlight.js | latest | Chat rendering |
| i18n | vue-i18n | 9+ | en/ru |
| HTTP | ofetch / native fetch | — | Типизированные запросы к API |
| Testing (unit) | Vitest | latest | Renderer, SDK |
| Testing (e2e) | Playwright (_electron) | latest | Electron E2E |
| Linting | Biome | latest | TS-native, быстрый |
| CI | GitHub Actions (Windows) | — | Build + test + package |

## 6. Структура проекта

```
src/hercules-studio/
├── electron/
│   ├── main/
│   │   ├── index.ts              # entry point, app lifecycle
│   │   ├── window.ts             # BrowserWindow creation, single-instance
│   │   ├── scanner.ts            # AgentScanner (ports + processes)
│   │   ├── connections.ts        # ConnectionManager (persist, health, checkin)
│   │   ├── native-bridge.ts      # fs, shell, safeStorage, notifications, tray
│   │   ├── sqlite.ts             # better-sqlite3 wrapper, migrations
│   │   ├── license.ts            # LicenseManager (consent, commercial)
│   │   └── ipc.ts                # IPC handlers, contextBridge setup
│   └── preload/
│       └── index.ts              # exposeAPI (typed, from shared/protocol.ts)
├── renderer/
│   ├── src/
│   │   ├── App.vue
│   │   ├── main.ts
│   │   ├── views/
│   │   │   ├── AgentList.vue
│   │   │   ├── DiscoverAgents.vue
│   │   │   ├── Chat.vue
│   │   │   ├── SkillEditor.vue
│   │   │   ├── SkillPush.vue
│   │   │   ├── MeshExplorer.vue
│   │   │   ├── Tools.vue
│   │   │   ├── McpManager.vue
│   │   │   ├── Config.vue
│   │   │   ├── Consensus.vue
│   │   │   ├── WorkflowDesigner.vue
│   │   │   └── EmptyState.vue
│   │   ├── components/
│   │   │   ├── layout/
│   │   │   │   ├── ActivityBar.vue
│   │   │   │   ├── Sidebar.vue
│   │   │   │   ├── MainWorkbench.vue
│   │   │   │   ├── StatusBar.vue
│   │   │   │   ├── BottomPanel.vue
│   │   │   │   └── CommandPalette.vue
│   │   │   ├── chat/
│   │   │   ├── skills/
│   │   │   ├── mesh/
│   │   │   ├── tools/
│   │   │   ├── config/
│   │   │   ├── consensus/
│   │   │   ├── workflow/
│   │   │   └── common/           # shadcn-vue components
│   │   ├── stores/
│   │   │   ├── connections.ts
│   │   │   ├── activeAgent.ts
│   │   │   ├── chat.ts
│   │   │   ├── skills.ts
│   │   │   ├── mesh.ts
│   │   │   ├── tools.ts
│   │   │   ├── config.ts
│   │   │   ├── workflow.ts
│   │   │   └── notifications.ts
│   │   ├── sdk/
│   │   │   ├── client.ts         # HerculesClient class
│   │   │   ├── types.ts          # DTO (ported from hercules-web api.ts)
│   │   │   └── endpoints/
│   │   │       ├── chat.ts
│   │   │       ├── skills.ts
│   │   │       ├── mesh.ts
│   │   │       ├── tools.ts
│   │   │       ├── mcp.ts
│   │   │       ├── config.ts
│   │   │       ├── system.ts     # checkin/checkout/restart
│   │   │       └── workflows.ts
│   │   ├── i18n/
│   │   │   ├── en.json
│   │   │   └── ru.json
│   │   ├── assets/
│   │   │   └── styles/
│   │   │       └── global.css    # Tailwind v4 entry, dark+light theme
│   │   └── composables/
│   │       ├── useConnection.ts
│   │       ├── useAgent.ts
│   │       └── useHotkeys.ts
│   ├── index.html
│   └── vite.config.ts
├── shared/
│   └── protocol.ts               # IPC contract (types shared main↔renderer)
├── resources/
│   ├── icon.ico
│   ├── tray-icon.ico
│   └── license-consent.md        # AGPL text + commercial terms
├── package.json
├── electron-builder.yml
├── electron.vite.config.ts       # electron-vite config (main + preload + renderer)
├── tsconfig.json
├── biome.json
└── README.md
```

## 7. Auth модель

### 7.1 Dual API keys (Agent side)

Агент генерирует при первом старте два ключа:

```json
// data/security/keys.json
{
  "contribute": "hc_contrib_<random>",
  "system": "hc_sys_<random>"
}
```

| Key | Role | Permissions |
|---|---|---|
| **contribute** | operator | chat, skills CRUD, memory, config GET, config PATCH (non-destructive), mesh read, tools enable/disable, marketplace install |
| **system** | admin | всё из contribute + restart, lifecycle destructive (decommission), config PUT (full replace), MCP add/remove, quota changes, force checkout |

`ApiKeyMiddleware` проверяет ключи из `WebApi:ApiKeys` массива:
```json
{
  "WebApi": {
    "ApiKeys": [
      { "key": "hc_contrib_...", "role": "contribute" },
      { "key": "hc_sys_...", "role": "system" }
    ]
  }
}
```

### 7.2 CheckIn/CheckOut

| Endpoint | Key | Описание |
|---|---|---|
| `POST /api/system/checkin` | contribute | CheckIn: Studio занимает агента. Body: `{studioId, studioName}`. Возвращает `{checkedOut: true, checkedOutBy: studioName}` или отказ |
| `POST /api/system/checkout` | contribute | Release агента |
| `POST /api/system/checkin/heartbeat` | contribute | Heartbeat (60s TTL). Если не пришла → auto checkout |
| `GET /api/system/checkin/status` | any | Status: `{checkedOut, checkedOutBy, checkedOutAt, ttlSeconds}` |
| `POST /api/system/checkin/force` | system | Force checkout (отобрать) |

**Поведение:**
- Contribute key = одна активная Studio. Вторая получает отказ "agent is checked out by {studioName}"
- System key может подключаться параллельно (read-only monitor mode)
- Crash Studio → heartbeat не пришла → TTL истёк → auto checkout

### 7.3 Studio side

- API keys хранятся в `safeStorage` (OS keychain), не в JSON
- `connections.json` хранит только `{name, baseUrl, agentId, displayName, authScheme}`
- System key вводится при необходимости system-операции → подтверждение → используется для этого запроса → индикатор "system mode" в UI
- Повышение в runtime: Studio отправляет system key для system-операций, не нужно второе connection

### 7.4 Workflow-server auth

- Workflow-server → Agent: `clientId/clientSecret` (упрощённый, без JWT)
- Workflow-server генерирует credentials при установке
- Studio регистрирует workflow-server credentials у агента (через `POST /api/mesh/agents/register`)
- Studio → Workflow-server: отдельный API key (генерируется workflow-server)

## 8. Локальное хранилище Studio

### 8.1 SQLite (`userData/studio.db`)

```sql
-- Chat history per connection
CREATE TABLE chat_history (
  id TEXT PRIMARY KEY,
  connection_id TEXT NOT NULL,
  agent_id TEXT NOT NULL,
  role TEXT NOT NULL,          -- user/assistant/system
  content TEXT NOT NULL,
  metadata TEXT,               -- JSON: mode, confidence, skill, provider
  created_at TEXT NOT NULL
);

-- Skill drafts (local, before push)
CREATE TABLE skill_drafts (
  id TEXT PRIMARY KEY,
  connection_id TEXT NOT NULL,
  skill_id TEXT,               -- null for new skills
  name TEXT NOT NULL,
  prompt_md TEXT,
  meta_json TEXT,
  description_md TEXT,
  csharp_files TEXT,           -- JSON array of {filename, content}
  updated_at TEXT NOT NULL,
  pushed_at TEXT
);

-- Workflow definitions (MVP, until workflow-server)
CREATE TABLE workflows (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  graph_json TEXT NOT NULL,    -- Vue Flow graph
  template BOOLEAN DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

-- Scan cache
CREATE TABLE scan_cache (
  port INTEGER PRIMARY KEY,
  agent_id TEXT,
  display_name TEXT,
  endpoint TEXT,
  auth_required BOOLEAN,
  found_at TEXT NOT NULL
);
```

### 8.2 JSON / safeStorage

| Файл | Хранилище | Содержание |
|---|---|---|
| `userData/connections.json` | FS | `[{name, baseUrl, agentId, displayName, authScheme, lastSeen}]` |
| `userData/settings.json` | FS | scan range, theme, language, typing effect, compact mode |
| Keychain entry | safeStorage | API keys (contribute + system per connection) |
| `userData/license-consent.json` | FS | `{consentVersion, acceptedAt, type: "nonprofit"|"commercial", licenseKey?}` |

## 9. IPC контракт (shared/protocol.ts)

```typescript
// shared/protocol.ts — типы shared между main и renderer

export interface IpcApi {
  // Connections
  connections: {
    list(): Promise<Connection[]>;
    add(conn: NewConnection): Promise<Connection>;
    remove(id: string): Promise<void>;
    update(id: string, patch: Partial<Connection>): Promise<Connection>;
    healthCheck(id: string): Promise<HealthStatus>;
  };
  // Scanner
  scanner: {
    scan(): Promise<DiscoveredAgent[]>;
    scanProgress(callback: (progress: ScanProgress) => void): void;
  };
  // Native
  native: {
    readFile(path: string): Promise<string>;
    writeFile(path: string, content: string): Promise<void>;
    openExternal(url: string): Promise<void>;
    showNotification(title: string, body: string): Promise<void>;
    setTray(icon: string, menu: TrayMenu): Promise<void>;
  };
  // SQLite
  db: {
    query<T>(sql: string, params?: unknown[]): Promise<T[]>;
    execute(sql: string, params?: unknown[]): Promise<Changes>;
  };
  // License
  license: {
    getConsent(): Promise<LicenseConsent | null>;
    acceptConsent(type: "nonprofit" | "commercial", key?: string): Promise<void>;
  };
  // System keys
  keys: {
    getSystemKey(connectionId: string): Promise<string | null>;
    setSystemKey(connectionId: string, key: string): Promise<void>;
    promptSystemKey(): Promise<string | null>;  // shows dialog
  };
}
```

## 10. i18n

en/ru с первого дня через `vue-i18n`. Все строки в `renderer/src/i18n/{en,ru}.json`.

## 11. Theme

Dark + Light, switchable. Dark = базовый (консистентность с hercules-web). Tailwind v4 с CSS variables для цветов.

## 12. Безопасность

- **contextIsolation: true** — renderer не имеет прямого доступа к Node.js
- **preload script** — exposes только `IpcApi` через `contextBridge`
- **CSP** — `script-src 'self'` в production
- **safeStorage** — API keys в OS keychain (Windows Credential Manager)
- **No remote modules** — `nodeIntegration: false`
- **Sandbox** — `sandbox: true` для renderer
- **C# skill pre-check** — DangerousCodeScanner на бэкенде перед push
- **SkillSdk whitelist** — file-based apps используют только разрешённые API

## 13. Риски и митигации

| Риск | Митигация |
|---|---|
| Electron ~150MB размер | Приемлемо для IDE; Tauri как backup |
| Range scan медленный | 8421-8521 (100 портов) + 50 concurrent + 300ms timeout = ~1s |
| Backend restart небезопасен | Supervisor-протокол: POST /api/system/restart → agent ставит флаг → Studio (или watcher) перезапускает |
| BPMN — большой scope | MVP: linear + conditional. Production: полный BPMN после MVP |
| MCP hot-reload не работает | Доработка McpClientService:IConfigReload на Stage 6 |
| Multi-agent API key management | safeStorage per connection, OS keychain |
| CORS при remote | Main process (Node) делает запросы, не браузер → CORS не применяется |
| C# LSP = тяжёлая зависимость | Базовая подсветка C# для MVP, LSP позже |
| Studio crash → checked out forever | Heartbeat TTL 60s → auto checkout |
| Workflow-server = отдельный сервис | Архитектура заложена в Stage 0, реализация в Stage 8 |

## 14. Связанные ADR

- [adr/0001-electron-over-tauri.md](adr/0001-electron-over-tauri.md)
- [adr/0002-vue3-over-react.md](adr/0002-vue3-over-react.md)
- [adr/0003-port-range-8421.md](adr/0003-port-range-8421.md)
- [adr/0004-dual-api-keys.md](adr/0004-dual-api-keys.md)
- [adr/0005-checkin-checkout.md](adr/0005-checkin-checkout.md)
- [adr/0006-sqlite-local-storage.md](adr/0006-sqlite-local-storage.md)
- [adr/0007-agpl-license.md](adr/0007-agpl-license.md)
- [adr/0008-workflow-server-architecture.md](adr/0008-workflow-server-architecture.md)