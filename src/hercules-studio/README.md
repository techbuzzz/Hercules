# Hercules Studio

> **IDE for managing, configuring, and orchestrating Hercules AI agents.**
> Built with Electron + Vue 3 + TypeScript.

[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](../../LICENSE)

## What is Hercules Studio?

Hercules Studio is a desktop IDE application for operators, developers, and architects who work with multiple Hercules AI agents. It connects to already-running agents via HTTP REST API, scans your machine for agents, and provides:

- **Multi-agent management** — connect to multiple agents, switch between them
- **Agent scanner** — discover agents on ports 8421-8521 + process scan
- **Chat** — conversation with any connected agent
- **Skill editor** — Monaco-based editor for prompts, meta, C# file-based apps
- **Skill push** — create and deploy skills to any agent
- **Mesh explorer** — visual topology graph, router, shared memory
- **Tool registry + MCP** — manage tools and MCP servers
- **Fine-grained config** — LLM providers, roles, quotas, context budget, restart
- **Consensus** — multi-agent parallel chat with LLM-judge aggregation
- **BPMN workflow designer** — visual multi-agent workflow orchestration

> **Full documentation:** [docs/EPIC_Hercules_Studio/](../../docs/EPIC_Hercules_Studio/README.md)

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| **Node.js** | ≥ 22.12 | Check with `node --version` |
| **npm** | ≥ 10 | Comes with Node.js |
| **Hercules agent** | running | Studio connects to an already-running agent on `http://localhost:8421` |
| **Windows** | 10/11 x64 | MVP target platform |
| **.NET SDK** | 10+ | Only needed to run the Hercules agent backend (not Studio itself) |

### Verify prerequisites

```powershell
node --version    # should be v22.12+
npm --version     # should be 10+
```

---

## Quick start (development)

### Option A: Web preview (no Electron, no native deps)

This mode runs the Vue renderer in a browser with a mock IPC API. Useful for UI development and quick preview without installing Electron or better-sqlite3.

```powershell
cd src\hercules-studio
npm install vue pinia vue-i18n --save --ignore-scripts --no-audit --no-fund
npm install --save-dev @vitejs/plugin-vue vite typescript vue-tsc --ignore-scripts --no-audit --no-fund
npm run dev
```

Open `http://localhost:4322` in your browser. The app runs with a mock API that stores data in `localStorage`.

### Option B: Full Electron dev mode

Requires Electron + better-sqlite3 native addon (may take 5-10 minutes to install).

```powershell
cd src\hercules-studio
npm install
npm run dev:electron
```

### Option C: Start a Hercules agent and connect

1. Start a Hercules agent:

```powershell
cd src\agent
dotnet run --project Hercules.WebApi
```

The agent starts on `http://localhost:8421` (after [task_096](../../docs/roadmap/tasks/completed/task_096.md) port migration) or `http://localhost:5000` (current default).

2. In Studio, click **"Scan for agents"** — discovers agents on ports 8421-8521 + 5000 (legacy)
3. Or click **"Add connection manually"** — enter URL + API key
4. Click **"Test connection"** to verify, then **"Save"**

---

## Build (production)

### Build the app

```powershell
npm run build
```

This runs `electron-vite build` which produces:
- `out/main/index.js` — main process bundle
- `out/preload/index.js` — preload script bundle
- `out/renderer/` — renderer (Vue 3) production build

### Package (installer + portable)

```powershell
# NSIS installer + portable zip
npm run package

# Windows only
npm run package:win

# Portable zip only
npm run package:portable
```

Output goes to `release/`:
- `Hercules-Studio-0.1.0-x64.exe` — NSIS installer
- `Hercules-Studio-0.1.0-portable.exe` — portable (no install needed)

---

## All npm scripts

| Script | Description |
|---|---|
| `npm run dev` | Web preview mode: Vite dev server (browser, mock API) |
| `npm run dev:electron` | Full Electron dev mode (requires Electron install) |
| `npm run build` | Production build (web: Vite build → dist/) |
| `npm run build:electron` | Production build (Electron: electron-vite build → out/) |
| `npm run preview` | Preview web build |
| `npm run typecheck` | TypeScript check (vue-tsc) |
| `npm run test` | Vitest unit tests |
| `npm run test:watch` | Vitest in watch mode |

---

## Project structure

```
src/hercules-studio/
├── electron/
│   ├── main/               # Main process (Node.js)
│   │   ├── index.ts        # App lifecycle, window creation
│   │   ├── ipc.ts          # IPC handlers (ipcMain.handle)
│   │   ├── sqlite.ts       # better-sqlite3 wrapper + migrations
│   │   ├── connections.ts  # Connection manager (persist, health, safeStorage)
│   │   ├── scanner.ts      # Agent scanner (port scan + process scan)
│   │   ├── license.ts      # License consent manager
│   │   └── settings.ts     # Studio settings manager
│   └── preload/
│       └── index.ts        # contextBridge (exposes studioAPI to renderer)
├── renderer/
│   ├── src/
│   │   ├── App.vue         # Root component (layout shell)
│   │   ├── main.ts         # Vue app init (Pinia, i18n, Tailwind)
│   │   ├── views/          # Top-level views
│   │   │   ├── EmptyState.vue       # Welcome + marketing carousel
│   │   │   └── AgentList.vue        # Agent management + scanner
│   │   ├── components/
│   │   │   ├── layout/     # ActivityBar, Sidebar, MainWorkbench, StatusBar, CommandPalette
│   │   │   └── common/     # LicenseDialog, shared UI
│   │   ├── stores/         # Pinia stores (connections, settings)
│   │   ├── sdk/            # HerculesClient (TypeScript API client)
│   │   │   ├── client.ts   # HTTP client to Hercules agent
│   │   │   └── types.ts    # DTOs (ported from hercules-web)
│   │   ├── i18n/           # en.json, ru.json
│   │   ├── assets/styles/  # global.css (Tailwind v4 + theme)
│   │   └── composables/    # Vue composables (useHotkeys, etc.)
│   └── index.html
├── shared/
│   └── protocol.ts         # IPC contract (types shared main↔renderer)
├── resources/              # Icons, license text
├── package.json
├── electron.vite.config.ts # electron-vite config (main + preload + renderer)
├── electron-builder.yml    # Packaging config (NSIS + portable)
├── tsconfig.json           # Root TS config (references node + web)
├── tsconfig.node.json      # TS config for main/preload/shared
├── tsconfig.web.json       # TS config for renderer (Vue)
├── vitest.config.ts        # Vitest unit test config
├── biome.json              # Biome linter/formatter config
└── .gitignore
```

---

## Tech stack

| Layer | Technology | Why |
|---|---|---|
| Desktop shell | Electron 30+ | Mature, Windows-first, Monaco, auto-updater |
| Build (dev) | electron-vite | HMR for renderer + main, TS-native |
| Build (package) | electron-builder | NSIS installer, portable zip |
| Main process | TypeScript | Typed IPC, shared types with renderer |
| Renderer | Vue 3 (Composition API) | Team preference, reactive, `<script setup>` |
| State | Pinia | Official Vue state management |
| Styling | Tailwind CSS 4 | Consistency with hercules-web |
| Editor | Monaco Editor | Skill/prompt/config editing |
| Terminal | xterm.js | C# test-run output, built-in terminal |
| Graph viz | Vue Flow | Mesh topology + BPMN designer |
| Local DB | better-sqlite3 | Chat history, skill drafts, workflows, scan cache |
| Icons | Tabler Icons | Licensed icon set |
| Markdown | markdown-it + highlight.js | Chat rendering |
| i18n | vue-i18n | en/ru from day one |
| Tests (unit) | Vitest | Renderer, SDK |
| Tests (e2e) | Playwright (Electron) | Full app E2E |
| Linting | Biome | TS-native, fast |

---

## Configuration

### Agent port

Studio scans ports **8421-8521** by default (Hercules agent range) + legacy port 5000.

Configure scan range in **Settings** (after Stage 0):
- `userData/settings.json` → `scan.portStart`, `scan.portEnd`, `scan.legacyPort`

### Agent API keys

- **Contribute key** — everyday operations (chat, skills, config read)
- **System key** — admin operations (restart, config PUT, MCP add)

Both stored in OS keychain via `safeStorage`. Enter system key when prompted for system operations.

### Studio settings

Located at `userData/settings.json`:
- `theme`: "dark" | "light"
- `locale`: "en" | "ru"
- `scan`: port range, process scan, auto-scan
- `notifications`: per-event-type toggles
- `autoUpdate`: boolean
- `minimizeToTray`: boolean

---

## Troubleshooting

### `npm install` fails on `better-sqlite3`

**Problem:** `electron-rebuild` fails to compile native addon.

**Solution:**
```powershell
# Install Windows Build Tools (if not already)
npm install --global windows-build-tools

# Or use Visual Studio Build Tools with C++ workload

# Then rebuild manually
npx electron-rebuild -f -w better-sqlite3
```

### Studio shows "No agent connected"

1. Verify agent is running: `curl http://localhost:8421/api/health`
2. Check agent URL in **Add Connection** form
3. Check API key matches agent's `contribute` key (printed in agent console)
4. Check firewall isn't blocking localhost

### License dialog appears every launch

**Problem:** `userData/license-consent.json` not saved or corrupted.

**Solution:**
- Check write permissions to `%APPDATA%/hercules-studio/`
- Delete `license-consent.json` and re-accept

### Port scan finds nothing

1. Verify agent is running on port 8421 (or 5000 legacy)
2. Try manual connection: enter `http://localhost:8421` + API key
3. Check if agent has `WebApi:ApiKey` configured — scanner probes manifest which requires auth

### Dev mode: blank window

1. Check Vite dev server started (look for `ELECTRON_RENDERER_URL` in console)
2. Open DevTools: `Ctrl+Shift+I` → check console errors
3. Verify `electron.vite.config.ts` paths are correct

---

## Development notes

### IPC contract

All IPC communication is typed via `shared/protocol.ts`:
- `IpcApi` — interface exposed to renderer via `window.studioAPI`
- `IpcChannels` — channel name constants (used by `ipcMain.handle` and `ipcRenderer.invoke`)
- Shared types: `Connection`, `DiscoveredAgent`, `LicenseConsent`, `StudioSettings`, etc.

**Never use untyped `ipcRenderer.send/on` — always go through `window.studioAPI`.**

### Adding a new IPC handler

1. Add channel to `IpcChannels` in `shared/protocol.ts`
2. Add method to `IpcApi` interface in `shared/protocol.ts`
3. Implement in `electron/main/ipc.ts`: `ipcMain.handle(channel, handler)`
4. Expose in `electron/preload/index.ts`: `ipcRenderer.invoke(channel, ...)`
5. Call from renderer: `await window.studioAPI.namespace.method(args)`

### Adding a new view

1. Create `renderer/src/views/MyView.vue`
2. Add to `MainWorkbench.vue` conditional render
3. Add activity bar entry in `ActivityBar.vue`
4. Add sidebar content in `Sidebar.vue`
5. Add i18n strings to `en.json` and `ru.json`

### Theme

Dark + Light themes via CSS variables in `global.css`:
- `:root` — light theme
- `.dark` — dark theme (applied to `<html>` by settings store)

---

## License

Hercules Studio is dual-licensed:

- **AGPL-3.0** — for non-commercial and personal use. See [LICENSE](../../LICENSE).
- **Commercial License** — required for corporate use (CRM/ECM integration, enterprise deployment). See [LICENSE-COMMERCIAL.md](../../docs/EPIC_Hercules_Studio/) (to be added in Stage 9).

---

## Roadmap

See [docs/EPIC_Hercules_Studio/ROADMAP.md](../../docs/EPIC_Hercules_Studio/ROADMAP.md) for the full 9-stage roadmap.

Current state: **Stage 0 (Skeleton)** — basic Electron + Vue + Vite + IPC + layout + SQLite + SDK + license consent + empty state + agent scanner + connection manager.

---

## Related

- [Hercules Agent](../agent/) — .NET backend (Hercules.WebApi)
- [Hercules Web](../hercules-web/) — lightweight web admin (Astro)
- [Epic Documentation](../../docs/EPIC_Hercules_Studio/README.md) — full Studio design
- [Backend Prerequisites](../../docs/roadmap/backlog.md#phase-8--hercules-studio-backend-prerequisites) — task_096-108