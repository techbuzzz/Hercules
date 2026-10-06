# Hercules Studio

Web IDE for managing Hercules AI agents.

Studio is a **browser application** — a Vue 3 + Vite SPA. The agent serves it at `/ui`,
so it runs same-origin and needs no desktop shell, installer or tray process.

Studio is the **single** front-end. `src/hercules-web` (Astro) is deprecated — see
[DEPRECATED.md](../hercules-web/DEPRECATED.md) for the panel-by-panel replacement map.

> **Migration note (2026-10-06).** Studio was an Electron app that never built; see
> [ADR-0009](../../docs/EPIC_Hercules_Studio/adr/0009-web-first-studio.md), which supersedes
> [ADR-0001](../../docs/EPIC_Hercules_Studio/adr/0001-electron-over-tauri.md). The Electron
> main process, preload bridge and packaging config have been removed. What replaced them
> is `renderer/src/platform/` — a typed capability contract with a browser implementation.

## Stack

- Vue 3 (`<script setup>`), Pinia, vue-i18n (en/ru)
- Vite 5, Tailwind CSS v4
- Vitest (unit), Playwright (E2E against Chromium)

## Getting started

```bash
npm install
npm run dev          # Vite dev server → http://localhost:4330/ui/
```

`4330` is deliberate: `hercules-web` owns `4322`. Both are in the agent's dev CORS
allowlist.

The `/ui/` path is deliberate too — it matches where the agent hosts the production
bundle, so the app is developed at the same base path it ships at.

To develop against the agent, add `http://localhost:4330` to `WebApi:AllowedCorsOrigins`
if you have overridden the defaults, then open Studio and connect.

## Scripts

| Command | Purpose |
|---|---|
| `npm run dev` | Vite dev server on port 4330 |
| `npm run build` | Production build into `dist/` |
| `npm run preview` | Serve the built bundle |
| `npm run typecheck` | `vue-tsc --noEmit` over `renderer/src` |
| `npm run build` | Production build — **also validates template expressions** (see below) |
| `npm test` | Vitest unit tests |
| `npm run test:watch` | Vitest in watch mode |
| `npm run test:e2e` | Playwright E2E against Chromium |
| `npm run lint` | Biome |

## How it connects to an agent

1. You enter a base URL and your **contribute** API key.
2. Studio calls `POST /api/studio/session` once with `X-Api-Key`.
3. The agent returns a short-lived opaque token. Studio holds it **in memory only**.
4. Every later request carries `X-Session-Token`.

**Your API key is never written to browser storage.** `hercules-studio.*` keys in
`localStorage` hold theme, locale, the connection list (without credentials) and drafts.
There is a unit test asserting no key material reaches `localStorage`.

When a session expires the UI prompts for the key again, via `ReauthDialog`. This is a
deliberate consequence of not persisting the key: losing the tab means re-entering it.

## Architecture

```
renderer/src/
├── platform/          # capability contract + browser implementation
│   ├── capabilities.ts  # PlatformCapabilities — the contract (replaces IpcApi)
│   ├── web.ts           # the browser implementation
│   └── index.ts         # resolution seam; the extension point for future adapters
├── sdk/client.ts      # HTTP client for the agent (session-token auth)
├── stores/            # Pinia: connections, settings, error, toast
└── views/             # one per registry entry in config/view-registry.ts
```

Nothing outside `platform/` touches the network directly. Views and stores depend on the
capability contract, never on a transport.

### Capabilities that no longer exist

These were `IpcApi` members dropped during the migration, because a browser cannot provide
them and the agent now does:

| Dropped | Replaced by |
|---|---|
| `native.setTray` | Nothing (PWA shortcut in a later phase) |
| `native.spawnTerminal` / `killTerminal` | Agent-side execution + output stream |
| `db.query` / `db.execute` | workflow-server / agent persistence |
| Port + process scanning | A2A discovery (`/agent.manifest.json`) |
| `keys.*` | `session.*` (exchange, never persist) |

## Testing

```bash
npm test        # unit: platform layer, storage resilience, credential handling
npm run build   # required — typecheck alone misses template parse errors
npm run test:e2e
```

`vue-tsc` does **not** report every template-expression error. A nested-quote bug like
`:placeholder="t("tools.search")"` typechecks clean and only fails at build time. Run
`npm run build` before calling a view done.

Unit tests cover the platform layer directly, including the invariants that matter most:
credential non-persistence, session expiry, corrupt-storage fallback, and normalisation of
the legacy Electron-era settings blob.

E2E runs against the built bundle in Chromium, with agent calls stubbed per spec.

## Licence

AGPL-3.0. First-run consent is captured by `LicenseDialog`.