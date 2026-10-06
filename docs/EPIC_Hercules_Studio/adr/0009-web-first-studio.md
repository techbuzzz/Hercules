# ADR-0009: Web-first Hercules Studio (supersedes ADR-0001)

**Status:** Accepted
**Date:** 2026-10-06
**Supersedes:** [ADR-0001 — Electron over Tauri](0001-electron-over-tauri.md)

## Context

ADR-0001 (2026-08-14) selected Electron as the desktop shell for Hercules Studio, on the
premise that Studio needed a native shell in order to:

- run a full IDE-like UI (Monaco, xterm.js, Vue Flow);
- access the local file system and spawn processes;
- bundle a native addon (`better-sqlite3`);
- ship as a Windows x64 installer with auto-update.

It explicitly rejected a web-only client: *"PWA / web-only: Cannot access FS, tray,
notifications, process spawning. Not suitable for IDE."*

That decision was never executed. As of this ADR, `src/hercules-studio` had five commits,
four of which were attempts to repair it, and `package.json` did not declare any of the
packages the code imported (`electron`, `electron-vite`, `better-sqlite3`,
`@tailwindcss/vite`, `vitest`, `@playwright/test`). `node_modules` had never been
installed. Seven of eight registered views were a 541-byte `PlaceholderView.vue`.

Meanwhile the agent backend absorbed the responsibilities ADR-0001 assigned to the shell:

| ADR-0001 rationale | Status as of this ADR |
|---|---|
| "Run C# skills locally in xterm.js" | `CodeExecution/DotnetFileBasedExecutor.cs` executes C# **inside the agent process**; `Program.cs` registers it as `ICodeExecutor`. The browser needs a stream, not a local PTY. |
| "Spawn/kill agent processes" | `SystemController` documents the supervisor as *"Studio / systemd / watcher"* — explicitly designed for an **external** supervisor, not a UI button. |
| "Scan ports 8421–8521 + process table" | Standards-based A2A discovery already exists: `/agent.manifest.json`, `POST /api/a2a/discover`, mDNS via `DiscoveryConfig`. |
| "Local SQLite (`studio.db`)" | `workflow-server` already ships `SqliteWorkflowDefinitionStore` plus a REST surface. |
| "OS keychain via `safeStorage`" | Keys already belong to the agent (`ApiKeyMiddleware`, dual contribute/system roles). A browser cannot use the OS keychain, so it has no reason to hold keys either. |
| "Native notifications" | Web Notification API. |

CORS, response compression for `text/event-stream`, and WebSocket upgrade limits were all
already configured. The one genuine gap was that the agent did not serve a UI.

## Decision

**Hercules Studio is a browser application.** It is a Vue 3 + Vite SPA served by the agent
itself at `/ui`, running same-origin. There is no desktop shell.

Concretely:

1. **Delete the Electron layer.** `electron/`, `electron.vite.config.ts`,
   `electron-builder.yml` are removed. The `window.studioAPI` preload contract is replaced
   by an explicit `renderer/src/platform/` capability contract (`capabilities.ts`,
   `web.ts`, `index.ts`) that a view or store imports.
2. **Process supervision moves out of the UI** into `Hercules.Supervisor`, a small .NET
   process implementing the `restart-pending` protocol the backend already specified.
3. **The API key never enters browser storage.** The client exchanges it once at
   `POST /api/studio/session` for a short-lived opaque token held only in tab memory.
   `ApiKeyMiddleware` accepts `X-Session-Token` with the same role semantics as
   `X-Api-Key`.
4. **Discovery is A2A-based**, not port/process scanning.
5. **Studio becomes the primary UI.** `hercules-web` (Astro, 19 panels) is **deprecated, not
   ported**: its panels overlapped the endpoints Studio already uses, and porting them all
   would have recreated the two-front-end problem this ADR exists to remove. See
   `src/hercules-web/DEPRECATED.md` for the replacement map.

## Later decisions (2026-10-06)

The two decisions below were put to the owner as explicit choices and **answered by the
owner** (questionnaire returned `explicitUserConfirmation: true`), not inferred from a
timeout default:

- **Chat stays request/response.** `ILLMClient.StreamAsync` already exists with provider
  failover, but it sits *below* `AgentCore`'s routing and below the tool-iteration loop
  (`RunWithToolsAsync`), which must parse the full LLM output before deciding whether it is
  a tool call. Streaming faithfully requires refactoring ~440 lines of the core request
  pipeline. A naive endpoint would stream tokens while silently losing skill routing,
  confidence, proposals, memory writes and budget accounting. The "stream only when no
  tools are configured" shortcut was considered and **rejected** — it is a regression
  against current behaviour. Decision: keep request/response; the typing effect is
  labelled as presentational. Evidence recorded in `ROADMAP.md`.
- **`hercules-web` is deprecated rather than ported.** Panels already covered by Studio are
  not duplicated; panels with no Studio equivalent are listed explicitly as a backlog in
  `src/hercules-web/DEPRECATED.md` rather than being quietly dropped. That file is
  deliberately explicit about **partial** coverage (MeshDashboard lacks topology/traffic,
  AgentCardPanel lacks full card detail) and about capabilities Studio does **not** have
  (MCP servers, security ops, trust admission, backup, rollout, quotas, SLO, mesh
  profiles/router, profile editor). Workflow execution remains a backend stub (task_105).

### Scope of the Stage 0–9 product roadmap — CONFIRMED in scope

Whether this migration is the *entire* goal, or whether Stage 0–9's functional requirements
(Monaco, Vue Flow, MCP CRUD, distillation/Postgres, consensus engine, BPMN designer, API
codegen) are also in scope, was asked twice on 2026-10-06. The first
questionnaire **auto-selected the recommended default on timeout** and was therefore not an
owner decision. It was re-asked with explicit confirmation required, and the owner answered:
**the Stage 0–9 roadmap IS in scope.**

At the time of that question the roadmap stood at "0 done / 243 open". That figure is
historical, not current: the stages named above — API codegen, Monaco, MCP CRUD, Vue Flow,
consensus, context distillation — have since been delivered. See
`VERIFICATION.md#what-remains` for the per-stage breakdown; only **task_105** is still open.

This ADR therefore governs *how Studio is built* (web-first), not *what it must eventually do*.
It does not retire Stage 0–9; they remain the working backlog. Tracked in `VERIFICATION.md`.

The capability contract, not any particular file, is the extension point. A future desktop
adapter would be selected in `platform/index.ts` without touching views or stores.

## Rationale

- **The shell was solving a solved problem.** Roughly 90% of the capability list ADR-0001
  justified a shell for is already server-side. The shell was the most expensive part of
  the stack guarding the least.
- **It never worked.** Not "was slow to build" — it never passed an `npm install`. Four
  commits titled "fix" are the record.
- **The frontend already decoupled itself.** `main.ts` imported `mock-api.ts`, which only
  installed itself when `window.studioAPI` was absent. The renderer had been running in a
  browser with localStorage stubs all along.
- **It concentrates risk.** One UI instead of two. `hercules-web` (19 panels) and Studio
  (one real view) were competing for the same investment.
- **A key in localStorage is a real vulnerability.** Any XSS on the Studio origin would
  exfiltrate an agent API key. Session exchange removes that entirely.

### Accepted losses

These are real and are accepted, not overlooked:

- **Interactive PTY.** Skill-run output arrives as a stream; there is no `stdin`.
  Mitigated by Supervisor writing agent stdout to a file.
- **Process-table inspection.** Replaced by Supervisor's PID registry and A2A discovery.
- **Tray icon.** Cosmetic. Recovered in the optional PWA phase as a shortcut.
- **Installer / auto-update.** The audience is a local dev tool for this repository.

## Consequences

- `WebApi:StudioUiPath` (optional; auto-detected as `src/hercules-studio/dist`) controls
  SPA hosting. Absent a build, `/ui` is simply not served.
- Sessions are in-memory: an agent restart invalidates every session and clients
  re-exchange. This is the intended failure mode.
- The agent has a static file provider, which must not be mounted in a way that shadows
  `/api/*`. Studio is scoped to `/ui` and registered before the API middlewares so asset
  requests bypass rate limiting, auth and drain.
- Studio's dev server moved 4322 → **4330**; 4322 belongs to `hercules-web`. Both are in
  the agent's CORS dev allowlist.

## Alternatives considered

- **Continue Electron.** Rejected: never built, 1 of 8 views implemented, native addon
  rebuild burden, and it duplicated backend capability.
- **Tauri 2.** Rejected: Rust is not in the team's stack. Note ADR-0001's native-addon
  argument is now weak — Tauri ships an official SQLite plugin — so the only remaining
  reason to reject it is team composition, not capability.
- **Wails v3.** Rejected: Go backend for a .NET team; v3 status is beta/alpha with
  conflicting sources.
- **Nuxt / any SSR meta-framework.** Rejected: Studio would still need `ssr: false`
  (File System Access API, Notification), and the IDE shell would still be hand-written,
  so the migration cost is not repaid. The one genuine Nuxt benefit — a Nitro BFF holding
  the agent key server-side — is delivered by the session endpoint instead. Revisit only
  if session exchange proves unworkably complex backend-side.
- **Web with no local layer (no Supervisor).** Rejected as too radical: restart automation
  and PID bookkeeping still need a process owner.