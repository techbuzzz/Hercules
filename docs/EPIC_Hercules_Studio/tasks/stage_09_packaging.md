# Stage 9 — Packaging & Polish

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 1-2 недели
**Dependencies (backend):** нет
**Dependencies (Studio):** Stage 8

## Goal

Production-ready Windows installer + tray icon + auto-updater + native notifications + CI + full tests + documentation.

## Tasks

### 9.1 — Tray icon

- [ ] `electron/main/tray.ts`:
  - Tray icon (Tabler icon "robot" or custom)
  - Context menu:
    - "Open Hercules Studio" → show/focus window
    - "Active agent: {name}" → status display
    - "Quick chat" → mini chat window (future) or focus main
    - "Settings" → open settings
    - "Quit" → graceful close (checkout all connections)
  - Minimize to tray on close (setting, default off)
  - Notification badge on tray if escalations pending

### 9.2 — Native notifications

- [ ] `electron/main/native-bridge.ts` (расширить):
  - `showNotification(title, body, onClick?)` → Electron Notification
  - Click → focus Studio window, navigate to relevant view
  - Settings: enable/disable, per-event-type (consensus, workflow, escalation, chat)
  - Already partially implemented in Stage 7 — finalize here

### 9.3 — Auto-updater

- [ ] `electron/main/updater.ts`:
  - `electron-updater` + GitHub Releases as update provider
  - Check for updates on startup + every 1 hour
  - "Update available" notification → "Download and install"
  - Download in background → "Restart to update" prompt
  - Settings: auto-update toggle (default on), check frequency
- [ ] `electron-builder.yml` — publish config (GitHub Releases provider)

### 9.4 — Packaging (electron-builder)

- [ ] `electron-builder.yml`:
  - Target: NSIS installer (Windows x64)
  - Portable: zip (for dev/power users)
  - App ID: `com.hercules.studio`
  - Product name: Hercules Studio
  - Icon: `resources/icon.ico`
  - Uninstall: clean userData on uninstall (optional, setting)
  - File associations: `.hcskill` (skill package), `.hcworkflow` (workflow file)
- [ ] Build scripts:
  - `npm run build` → electron-vite build
  - `npm run package` → electron-builder (NSIS + portable)
  - `npm run release` → build + publish to GitHub Releases (CI)

### 9.5 — CI/CD (GitHub Actions)

- [ ] `.github/workflows/studio-ci.yml` (расширить из Stage 0):
  - Trigger: push to `main` / PR to `main` (paths: `src/hercules-studio/**`)
  - Job: `build-test-package` (windows-latest):
    - Node 22, npm install
    - `biome check`
    - `vitest run`
    - `electron-vite build`
    - `electron-builder` (NSIS + portable)
    - Upload artifacts (installer, portable zip)
  - Job: `release` (on tag `studio-v*`):
    - Build + package
    - Publish to GitHub Releases
    - Auto-updater files (latest.yml)

### 9.6 — Tests (full)

- [ ] Unit tests (Vitest):
  - SDK: all endpoint groups, DTO parsing
  - Stores: connections, chat, skills, mesh, tools, mcp, config, consensus, workflow
  - Utils: validation, expression evaluator, formatters
- [ ] E2E tests (Playwright for Electron):
  - Smoke: launch → license → empty state → add connection → chat
  - Skills: create → edit → push → see in list
  - Mesh: open explorer → see graph
  - Tools: toggle enable/disable
  - Config: edit LLM → save → hot-reload
  - Consensus: select agents → send → aggregate
  - Workflow (MVP): design → run → monitor
- [ ] Coverage: ≥60% for SDK, ≥40% for stores, critical paths covered

### 9.7 — Documentation

- [ ] `src/hercules-studio/README.md` (en + ru):
  - What is Hercules Studio
  - Prerequisites (Node 22, Hercules agent running)
  - Install (download installer / portable)
  - Dev setup (npm install, npm run dev)
  - Build (npm run build, npm run package)
  - Architecture overview (link to SYSTEM-DESIGN.md)
  - License (AGPL + commercial)
- [ ] `docs/EPIC_Hercules_Studio/USER-GUIDE-EN.md`:
  - Quickstart: start agent → scan → connect → chat
  - Skills: create, edit, push, templates
  - Mesh: explorer, router, shared memory
  - Tools: enable/disable, MCP management
  - Config: LLM, roles, quotas, context, restart
  - Consensus: multi-agent chat
  - Workflow: design, run, monitor (MVP)
  - Settings: theme, language, scan range, notifications
- [ ] `docs/EPIC_Hercules_Studio/USER-GUIDE-RU.md` — русский перевод
- [ ] `LICENSE` — AGPL-3.0 full text
- [ ] `LICENSE-COMMERCIAL.md` — commercial license terms

### 9.8 — Settings view

- [ ] `views/Settings.vue` (tabbed):
  - **General:** theme (dark/light), language (en/ru), compact mode
  - **Scanner:** port range, legacy 5000, process scan, auto-scan, concurrent, timeout
  - **Notifications:** enable native, per-event-type toggles
  - **Updates:** auto-update toggle, check frequency, current version
  - **License:** current consent type, "Enter commercial key" button
  - **About:** version, agent compatibility, links (docs, GitHub, license)
- [ ] Persisted in `userData/settings.json`

### 9.9 — Keyboard shortcuts (final)

- [ ] `composables/useHotkeys.ts` (расширить):
  - Ctrl+S — save (skill/config)
  - Ctrl+Enter — send (chat/consensus)
  - Ctrl+Shift+P — command palette
  - Ctrl+K — quick command
  - Ctrl+N — new (skill/workflow/connection)
  - Ctrl+O — open (skill/workflow file)
  - Ctrl+W — close tab
  - Ctrl+Tab — next tab
  - Ctrl+Shift+Tab — prev tab
  - F1 — help
  - Ctrl+, — settings
- [ ] Settings: view/modify hotkeys (future, hardcoded for MVP)

### 9.10 — Polish

- [ ] Empty states for all views (when no data): helpful messages + actions
- [ ] Error boundaries: catch renderer errors, show error page, report
- [ ] Loading states: skeletons for async data
- [ ] Tooltips on all icons/buttons
- [ ] Responsive layout: min 1024px, graceful degradation
- [ ] Accessibility: ARIA labels, keyboard navigation, focus management

## Acceptance criteria

- [ ] Tray icon: present, context menu works, minimize-to-tray optional
- [ ] Notifications: native, clickable, per-type settings
- [ ] Auto-updater: checks GitHub Releases, downloads, prompts restart
- [ ] Packaging: NSIS installer builds, portable zip builds
- [ ] CI: GitHub Actions runs build + test + package, uploads artifacts
- [ ] Release: tag → publish to GitHub Releases
- [ ] Unit tests: ≥60% SDK, ≥40% stores
- [ ] E2E tests: all critical paths covered
- [ ] Documentation: README (en+ru), User Guide (en+ru), LICENSE, LICENSE-COMMERCIAL
- [ ] Settings: all tabs work, persisted
- [ ] Hotkeys: all registered, work as expected
- [ ] Empty states: all views have helpful empty state
- [ ] Error boundaries: renderer errors don't crash app
- [ ] `npm run build` + `npm run package` + tests pass

## Scope / Likely files

- `electron/main/tray.ts`, `electron/main/updater.ts`, `electron/main/native-bridge.ts`
- `electron-builder.yml`
- `.github/workflows/studio-ci.yml`
- `renderer/src/views/Settings.vue`
- `src/hercules-studio/README.md`
- `docs/EPIC_Hercules_Studio/USER-GUIDE-EN.md`, `USER-GUIDE-RU.md`
- `LICENSE`, `LICENSE-COMMERCIAL.md`

## Dependencies

- **Backend:** нет
- **Studio:** Stage 8 (all features complete)

## Links

- Epic: [../README.md](../README.md)
- System Design: [../SYSTEM-DESIGN.md](../SYSTEM-DESIGN.md)
- Roadmap: [../ROADMAP.md](../ROADMAP.md)