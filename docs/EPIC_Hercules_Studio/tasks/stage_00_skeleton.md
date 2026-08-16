# Stage 0 — Skeleton

**Epic:** Hercules Studio
**Status:** in_progress (boilerplate created)
**Estimate:** 1-2 недели
**Dependencies (backend):** нет
**Dependencies (Studio):** нет (стартовый)
**Branch:** `feat/hercules-studio-stage-0`

## Goal

Запускаемый skeleton Electron + Vue 3 + Vite + IPC + VS Code-like layout + SQLite + SDK + license consent + empty state с marketing carousel. Studio запускается, можно добавить агента по URL, виден manifest.

## Boilerplate status

Initial boilerplate created at `src/hercules-studio/` (42 files, ~90KB). See [README.md](../../../src/hercules-studio/README.md) for build & run instructions.

### What's already done

- [x] Project structure (electron/main, electron/preload, renderer/src, shared, resources)
- [x] `package.json` — all dependencies (Electron 30, Vue 3.4, Pinia, vue-i18n, Monaco, xterm, Vue Flow, better-sqlite3, Tabler Icons, markdown-it, Vitest, Playwright, Biome)
- [x] `electron.vite.config.ts` — main + preload + renderer configs
- [x] `electron-builder.yml` — NSIS + portable, GitHub Releases publish
- [x] `tsconfig.json` + `tsconfig.node.json` + `tsconfig.web.json` — strict, path aliases
- [x] `biome.json` — linter/formatter config
- [x] `vitest.config.ts` — unit test config
- [x] `playwright.config.ts` — E2E config
- [x] `.gitignore`, `.env.example`

### What's already done — Main process

- [x] `electron/main/index.ts` — app lifecycle, BrowserWindow (contextIsolation, sandbox, no nodeIntegration), single-instance lock
- [x] `electron/main/ipc.ts` — all ipcMain.handle (connections, scanner, native, db, license, settings, keys, app)
- [x] `electron/main/sqlite.ts` — better-sqlite3 wrapper + migration v1 (chat_history, skill_drafts, workflows, scan_cache)
- [x] `electron/main/connections.ts` — Connection manager (add/remove/list/update, safeStorage for keys, health probe, JSON persist)
- [x] `electron/main/scanner.ts` — Agent scanner (port range 8421-8521 + legacy 5000, concurrent=50, timeout=300ms, process scan via tasklist+netstat)
- [x] `electron/main/license.ts` — License consent (JSON persist, consent version)
- [x] `electron/main/settings.ts` — Studio settings (theme, locale, scan config, notifications, auto-update)

### What's already done — Preload

- [x] `electron/preload/index.ts` — contextBridge.exposeInMainWorld('studioAPI', api) with full typed IpcApi

### What's already done — Shared

- [x] `shared/protocol.ts` — IpcApi interface, IpcChannels constants, all types (Connection, DiscoveredAgent, ScanProgress, LicenseConsent, StudioSettings, etc.)

### What's already done — Renderer

- [x] `renderer/index.html` — CSP, root div
- [x] `renderer/src/main.ts` — Vue init (Pinia, vue-i18n, Tailwind import)
- [x] `renderer/src/env.d.ts` — Vue + Window.studioAPI type declarations
- [x] `renderer/src/App.vue` — layout shell (ActivityBar + Sidebar + MainWorkbench + StatusBar + LicenseDialog + CommandPalette)
- [x] `renderer/src/assets/styles/global.css` — Tailwind v4 + dark/light theme CSS variables
- [x] Layout components:
  - [x] `ActivityBar.vue` — 8 icons (Agents, Chat, Skills, Mesh, Tools, Config, Workflow, Consensus) with Tabler Icons + tooltips
  - [x] `Sidebar.vue` — context content (agents list with online/offline groups)
  - [x] `MainWorkbench.vue` — view router (EmptyState / AgentList / placeholder)
  - [x] `StatusBar.vue` — active agent, status indicator, version
  - [x] `CommandPalette.vue` — Ctrl+Shift+P simple variant
- [x] `LicenseDialog.vue` — first-run consent (AGPL non-profit / commercial key / decline)
- [x] `EmptyState.vue` — welcome + marketing carousel (4 slides, dots) + quickstart + scan/add buttons
- [x] `AgentList.vue` — agent management (scan, discovered list, add form with test+save, connections list online/offline)
- [x] Pinia stores:
  - [x] `stores/connections.ts` — list, add, remove, setActive, healthCheck, scan, active, onlineCount
  - [x] `stores/settings.ts` — theme, locale, scan, notifications, applyTheme
- [x] SDK:
  - [x] `sdk/client.ts` — HerculesClient (chat, skills CRUD, improve, evaluate, stats, config GET/PATCH/PUT, mesh dashboard/agents, health, manifest, system key support)
  - [x] `sdk/types.ts` — DTOs (SkillDto, SkillDetailDto, ChatResponseDto, StatsDto, ConfigDto, AgentManifest, MeshAgentDto, MeshHealthDto, MeshDashboardDto)
- [x] Composables:
  - [x] `composables/useHotkeys.ts` — useHotkeys + useGlobalHotkeys (Ctrl+S, Ctrl+Enter, Ctrl+Shift+P, Ctrl+K, etc.)
  - [x] `composables/useConnection.ts` — useActiveAgent + useConnection
- [x] i18n: `en.json` + `ru.json` (all UI strings translated)

### What's already done — Tests

- [x] `vitest.config.ts` — renderer unit test config
- [x] `renderer/src/stores/connections.test.ts` — IpcChannels unit tests
- [x] `renderer/src/sdk/client.test.ts` — HerculesClient construction, system key tests
- [x] `playwright.config.ts` — E2E config
- [x] `tests/e2e/smoke.spec.ts` — smoke tests (license dialog → accept → empty state; activity bar 8 items)

### What's already done — CI

- [x] `.github/workflows/studio-ci.yml` — GitHub Actions (Windows): install, biome, typecheck, vitest, build, package, upload artifacts, release on tag

### What's already done — Docs

- [x] `README.md` — full build & run manual (prerequisites, quick start, all npm scripts, project structure, tech stack, configuration, troubleshooting, dev notes)
- [x] `resources/license-consent.md` — AGPL-3.0 + commercial terms

## Remaining tasks

### 0.10 — Verify build works

- [ ] `npm install` succeeds (including electron-rebuild for better-sqlite3)
- [ ] `npm run dev` launches Studio (HMR + Electron window)
- [ ] `npm run build` produces out/main, out/preload, out/renderer
- [ ] `npm run typecheck` passes (strict TS)
- [ ] `npm run lint:check` passes (Biome)
- [ ] `npm run test` passes (Vitest unit)
- [ ] `npm run package:win` produces NSIS installer + portable

### 0.11 — Fix runtime issues

- [ ] ActivityBar tooltip: verify `z-50` tooltip renders above sidebar
- [ ] CSP: verify `connect-src` allows agent URLs (http://localhost:8421 etc.)
- [ ] Scanner: test against real running agent on 8421 (or 5000 legacy)
- [ ] Connection form: verify health probe + save + sidebar update
- [ ] License dialog: verify first-run shows, second-run skips, consent version mismatch re-shows
- [ ] EmptyState carousel: verify dots navigation works
- [ ] CommandPalette: verify Ctrl+Shift+P opens, Escape closes

### 0.12 — Missing pieces to add

- [ ] **API key retrieval for SDK**: renderer needs API key to create HerculesClient. Currently keys are in safeStorage (main process). Add IPC method `keys:getContributeKey(connectionId)` → returns decrypted key from safeStorage. Store creates HerculesClient on setActive.
- [ ] **Terminal output forwarding**: `ipc.ts` has spawn handler but doesn't forward stdout/stderr to renderer. Add `webContents.send(NATIVE_TERMINAL_OUTPUT, pid, data)` in spawn handler.
- [ ] **Scanner progress forwarding**: `scanner.ts` has progressCallback but it's not wired to IPC. Add `webContents.send(SCANNER_PROGRESS, progress)` in scan handler.
- [ ] **Settings theme application**: `settings.ts` store has `applyTheme()` but it's not called on app load. Wire in `App.vue` onMounted.
- [ ] **Settings locale application**: `settings.ts` store loads locale but doesn't apply to vue-i18n. Wire `i18n.global.locale.value = settings.data.locale`.
- [ ] **Auto-scan on startup**: `App.vue` calls `connections.scan()` but doesn't pass progress callback. Wire scanner progress to a UI indicator.
- [ ] **Add Connection form: pre-fill from discovered**: `AgentList.vue` has `addDiscovered()` but doesn't auto-fill API key field (discovered agents may need auth). Add hint "Enter API key for this agent".

### 0.13 — Polish

- [ ] Window icon (`resources/icon.ico`) — create or use placeholder
- [ ] Tray icon placeholder (`resources/tray-icon.ico`) — for Stage 9
- [ ] `app.config.ts` or similar — document all configurable settings
- [ ] `CHANGELOG` entry for Studio 0.1.0
- [ ] Update root `.gitignore` to include `src/hercules-studio/node_modules/` and `src/hercules-studio/dist/`

## Acceptance criteria

- [ ] `npm run dev` запускает Studio, открывается окно 1280×800
- [ ] При первом запуске — LicenseDialog → Accept → EmptyState
- [ ] При втором запуске — LicenseDialog не показывается (consent сохранён)
- [ ] EmptyState: marketing carousel работает (точки), кнопки "Scan" и "Add" видимы
- [ ] Add Connection form: ввод URL + key → Test → Save → активный агент в StatusBar
- [ ] ActivityBar: 8 иконок, клик переключает sidebar content
- [ ] StatusBar: показывает активного агента или "No agent connected", версию Studio
- [ ] CommandPalette: Ctrl+Shift+P открывает, Escape закрывает
- [ ] Hotkeys: Ctrl+S, Ctrl+Enter, Ctrl+K (зарегистрированы через useHotkeys)
- [ ] i18n: RU/EN переключается, все строки переведены
- [ ] Theme: dark/light переключается (через settings store)
- [ ] SQLite: `userData/studio.db` создаётся, таблицы существуют
- [ ] `npm run build` собирает без ошибок
- [ ] `npm run typecheck` проходит
- [ ] `biome check` проходит
- [ ] Vitest: unit tests проходят
- [ ] Playwright: E2E smoke test проходит (после build)

## Scope / Likely files

`src/hercules-studio/` (вся структура из [SYSTEM-DESIGN.md §6](../SYSTEM-DESIGN.md#6-структура-проекта))

## Dependencies

- нет (стартовый этап)

## Risks / Rollback

- **electron-vite + better-sqlite3 native rebuild:** нужен `electron-rebuild`. Postinstall script в package.json. Если fails — manual `npx electron-rebuild -f -w better-sqlite3`.
- **Monaco в Electron:** большой бандл. Mitigation: lazy-load Monaco только когда SkillEditor открывается (Stage 2).
- **Vue Flow + Tailwind v4:** возможны конфликты стилей. Mitigation: scoped styles для Vue Flow.
- **CSP `connect-src`:** должен разрешать agent URLs. Current: `http://localhost:* http://127.0.0.1:*`. Для remote agents — нужно расширить или использовать main process proxy.

## Implementation notes

### 2026-08-14 — Boilerplate created
Initial boilerplate with 42 files. Main process, preload, renderer, shared protocol, stores, SDK, views, i18n, tests, CI all scaffolded. Remaining: verify build, fix runtime issues, add missing IPC wiring (key retrieval, terminal output, scanner progress), polish.

## Links

- Epic: [../README.md](../README.md)
- System Design: [../SYSTEM-DESIGN.md](../SYSTEM-DESIGN.md)
- Architecture: [../ARCHITECTURE.md](../ARCHITECTURE.md)
- Roadmap: [../ROADMAP.md](../ROADMAP.md)
- Studio README (build & run): [../../../src/hercules-studio/README.md](../../../src/hercules-studio/README.md)

- [ ] Создать `src/hercules-studio/` структуру (см. [SYSTEM-DESIGN.md §6](../SYSTEM-DESIGN.md#6-структура-проекта))
- [ ] `package.json` — name `hercules-studio`, version `0.1.0`, engines node >=22.12
- [ ] Зависимости:
  - `electron` ^30
  - `electron-vite` (dev)
  - `vue` ^3.4, `pinia` ^2, `vue-i18n` ^9
  - `vue-flow` (Vue Flow для graph viz)
  - `monaco-editor` + `@guolao/vue-monaco-editor` (Vue wrapper)
  - `xterm` + `xterm-addon-fit`
  - `better-sqlite3` + `@types/better-sqlite3`
  - `markdown-it`, `highlight.js`
  - `ofetch`
  - `@tabler/icons-vue` (Tabler Icons)
  - `tailwindcss` ^4, `@tailwindcss/vite`
  - `electron-builder` (dev, packaging)
  - `vitest`, `@playwright/test` (dev, tests)
  - `@biomejs/biome` (dev, lint)
  - `typescript` ^5.9
- [ ] `tsconfig.json` — strict mode, paths для `@shared/*`, `@renderer/*`
- [ ] `biome.json` — lint config
- [ ] `electron.vite.config.ts` — main + preload + renderer configs
- [ ] `.gitignore` — node_modules, dist, *.log

### 0.2 — Main process

- [ ] `electron/main/index.ts` — app lifecycle, single-instance lock, BrowserWindow creation (1280×800, dark background)
- [ ] `electron/main/window.ts` — window config: contextIsolation=true, nodeIntegration=false, sandbox=true, preload path
- [ ] `electron/main/sqlite.ts` — better-sqlite3 wrapper:
  - Open `userData/studio.db`
  - Migrations: create tables `chat_history`, `skill_drafts`, `workflows`, `scan_cache` (см. [SYSTEM-DESIGN.md §8.1](../SYSTEM-DESIGN.md#81-sqlite-userdatstudiodb))
  - `query<T>()`, `execute()` methods
- [ ] `electron/main/license.ts` — LicenseManager:
  - `getConsent()` — read `userData/license-consent.json`
  - `acceptConsent(type, key?)` — write consent
  - consentVersion = 1
- [ ] `electron/main/ipc.ts` — register all `ipcMain.handle()` for IpcApi
- [ ] `electron/main/native-bridge.ts` — stubs для fs, shell, notifications, tray (full impl в Stage 9)
- [ ] `electron/preload/index.ts` — `contextBridge.exposeInMainWorld('studioAPI', {...})` с typed IpcApi

### 0.3 — Shared protocol

- [ ] `shared/protocol.ts` — IpcApi interface (см. [SYSTEM-DESIGN.md §9](../SYSTEM-DESIGN.md#9-ipc-контракт-sharedprotocolts))
  - connections, scanner, native, db, license, keys namespaces
  - Все типы: Connection, NewConnection, HealthStatus, DiscoveredAgent, ScanProgress, LicenseConsent

### 0.4 — Renderer skeleton

- [ ] `renderer/index.html` — root HTML
- [ ] `renderer/src/main.ts` — Vue app init, Pinia, vue-i18n, Tailwind import
- [ ] `renderer/src/App.vue` — root component: layout shell
- [ ] Layout components:
  - `components/layout/ActivityBar.vue` — вертикальный бар: Agents, Chat, Skills, Mesh, Tools, Config, Workflow, Consensus (Tabler icons)
  - `components/layout/Sidebar.vue` — контекстный контент (placeholder для Stage 0)
  - `components/layout/MainWorkbench.vue` — табы с views (placeholder)
  - `components/layout/StatusBar.vue` — placeholder: "No agent connected", версия Studio
  - `components/layout/BottomPanel.vue` — placeholder (xterm.js integration в Stage 6)
  - `components/layout/CommandPalette.vue` — простой вариант (Ctrl+Shift+P), список команд (placeholder)
- [ ] Global CSS: `assets/styles/global.css` — Tailwind v4 entry, dark+light theme variables (см. [ARCHITECTURE.md §12](../ARCHITECTURE.md#12-theme-architecture))
- [ ] shadcn-vue: установить base components (Button, Input, Dialog, Toast, Card, Tabs, Badge, Tooltip) в `components/common/`

### 0.5 — Pinia stores

- [ ] `stores/connections.ts` — list, add, remove, update, active connection
- [ ] `stores/settings.ts` — theme (dark/light), locale (en/ru), typingEffect, compactMode, scanRange
- [ ] `stores/notifications.ts` — toasts, errors

### 0.6 — SDK (HerculesClient)

- [ ] `sdk/client.ts` — `HerculesClient` class:
  - constructor(baseUrl, apiKey)
  - `setSystemKey(key)` — elevate for system operations
  - `getManifest()` — GET /agent.manifest.json
  - `getHealth()` — GET /api/health
  - methods for all endpoint groups (chat, skills, mesh, tools, mcp, config, system)
- [ ] `sdk/types.ts` — портировать DTO из `src/hercules-web/src/lib/api.ts` (SkillDto, ChatResponseDto, StatsDto, MeshDto, ConfigDto, etc.)
- [ ] `sdk/endpoints/` — разбить по файлам: chat.ts, skills.ts, mesh.ts, tools.ts, mcp.ts, config.ts, system.ts, workflows.ts

### 0.7 — i18n

- [ ] `i18n/en.json` — все строки UI (ActivityBar labels, StatusBar, EmptyState, LicenseDialog, common actions)
- [ ] `i18n/ru.json` — русский перевод
- [ ] `composables/useHotkeys.ts` — Ctrl+S (save), Ctrl+Enter (send), Ctrl+Shift+P (palette), Ctrl+K (quick command)

### 0.8 — Empty state + License consent

- [ ] `views/EmptyState.vue` — Welcome screen:
  - "Welcome to Hercules Studio" заголовок
  - Marketing carousel (4-5 слайдов): скриншоты фич + преимущества + quickstart steps
  - Ручная навигация (точки/стрелки), данные захардкожены
  - Кнопки: "Scan for agents" (placeholder → Stage 1), "Add connection manually" (→ Add Connection form)
  - Quickstart guide (3 steps: 1. Start agent, 2. Scan, 3. Connect)
- [ ] `components/common/LicenseDialog.vue` — first-run consent:
  - AGPL-3.0 текст (из `resources/license-consent.md`)
  - Чекбокс "I use for non-commercial/personal"
  - Кнопки: "Accept (Non-profit)", "I have a commercial license key" (→ input), "Decline" (→ close app)
- [ ] App.vue: при старте проверить `license.getConsent()` → если null или consentVersion mismatch → показать LicenseDialog

### 0.9 — Add Connection form (базово)

- [ ] `components/connections/AddConnectionForm.vue` — форма:
  - Поля: name, baseUrl (default http://localhost:8421), apiKey
  - "Test connection" → `sdk.getHealth()` + `sdk.getManifest()` → validate agentId
  - "Save" → `connections.add()` (через IPC → main process → safeStorage + connections.json)
- [ ] При успешном add → StatusBar показывает активного агента (name + status online)

### 0.10 — Dev/Build config

- [ ] `electron.vite.config.ts` — main, preload, renderer configs
- [ ] Dev: `npm run dev` → electron-vite dev → HMR renderer + watch main/preload → launch Electron
- [ ] Build: `npm run build` → electron-vite build → dist/main, dist/preload, dist/renderer
- [ ] Package: `npm run package` → electron-builder (NSIS + portable) — config в `electron-builder.yml` (full packaging в Stage 9, здесь только stub)

### 0.11 — Tests setup

- [ ] Vitest config — renderer unit tests
- [ ] Playwright config — Electron E2E (`_electron.spawn()`)
- [ ] Один smoke test: Studio launches, license dialog appears, accept → empty state visible

### 0.12 — CI

- [ ] `.github/workflows/studio-ci.yml` — на push `src/hercules-studio/**`:
  - windows-latest, Node 22
  - npm install, biome check, vitest run, electron-vite build
  - upload artifacts (dist/)

## Acceptance criteria

- [ ] `npm run dev` запускает Studio, открывается окно 1280×800
- [ ] При первом запуске — LicenseDialog → Accept → EmptyState
- [ ] При втором запуске — LicenseDialog не показывается (consent сохранён)
- [ ] EmptyState: marketing carousel работает (стрелки/точки), кнопки "Scan" и "Add" видимы
- [ ] Add Connection form: ввод URL + key → Test → Save → активный агент в StatusBar
- [ ] ActivityBar: 8 иконок, клик переключает sidebar content (placeholder)
- [ ] StatusBar: показывает активного агента или "No agent connected", версию Studio
- [ ] CommandPalette: Ctrl+Shift+P открывает, показывает список команд (placeholder)
- [ ] Hotkeys: Ctrl+S, Ctrl+Enter, Ctrl+K (зарегистрированы, actions = placeholder)
- [ ] i18n: RU/EN переключается, все строки переведены
- [ ] Theme: dark/light переключается
- [ ] SQLite: `userData/studio.db` создаётся, таблицы существуют
- [ ] `npm run build` собирает без ошибок
- [ ] `biome check` проходит
- [ ] Vitest: smoke test проходит
- [ ] Playwright: E2E smoke test проходит

## Scope / Likely files

`src/hercules-studio/` (вся структура из [SYSTEM-DESIGN.md §6](../SYSTEM-DESIGN.md#6-структура-проекта))

## Dependencies

- нет (стартовый этап)

## Risks / Rollback

- **electron-vite + better-sqlite3 native rebuild:**可能 нужен `electron-rebuild`. Mitigation: documented in package.json postinstall.
- **Monaco в Electron:** большой бандл. Mitigation: lazy-load Monaco только когда SkillEditor открывается.
- **Vue Flow + Tailwind v4:** возможны конфликты стилей. Mitigation: scoped styles для Vue Flow.

## Links

- Epic: [../README.md](../README.md)
- System Design: [../SYSTEM-DESIGN.md](../SYSTEM-DESIGN.md)
- Architecture: [../ARCHITECTURE.md](../ARCHITECTURE.md)
- Roadmap: [../ROADMAP.md](../ROADMAP.md)