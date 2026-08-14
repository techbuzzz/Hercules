# Stage 0 — Skeleton

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 1-2 недели
**Dependencies (backend):** нет
**Dependencies (Studio):** нет (стартовый)

## Goal

Запускаемый skeleton Electron + Vue 3 + Vite + IPC + VS Code-like layout + SQLite + SDK + license consent + empty state с marketing carousel. Studio запускается, можно добавить агента по URL, виден manifest.

## Tasks

### 0.1 — Инициализация проекта

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