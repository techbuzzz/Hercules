# Hercules Studio migration — verification record

Date: **2026-10-06**. Machine: Windows, `node v22.22.0`, `.NET 10.0.12`.
Branch: `tasks/studio-ui`. Nothing was committed; this records state of the working tree.

This file exists so the migration's verification is **inspectable**, not merely asserted.

## What was verified, and how

Every command below was run in this working tree on the date above.

| # | Command | Result |
|---|---|---|
| 1 | `npm run typecheck` (studio) | 0 errors (`vue-tsc --noEmit -p tsconfig.web.json`) |
| 2 | `npm test` (studio) | **24 passed / 0 failed** (1 test file) |
| 3 | `npm run build` (studio) | `✓ built in 3.12s` — 8 lazy view chunks |
| 4 | `npm run test:e2e` (studio) | **12 passed** (27.8s), Chromium |
| 5 | `dotnet test tests/Hercules.Agent.Tests` | **2233 passed / 0 failed** (2m55s) |
| 6 | Agent runtime, live HTTP | `/api/health` 200 · `/ui/` 200 · `/ui/manifest.webmanifest` 200 · `/ui/sw.js` 200 · `/agent-card.json` 200 · `/api/stats` 401 (auth required — expected) |
| 7 | Supervisor restart cycle, live | pid 24384 → 14804, `state: running`, `crashCount: 0`, restart flag `pending: false` |
| 8 | `npm ci` (clean install) | `added 113 packages` — **`package-lock.json` is in sync with `package.json`** |
| 9 | `dotnet build src/agent/Hercules.slnx` | succeeded, 0 warnings, 0 errors — solution includes `Hercules.Supervisor` |

Rows 6–7 are **manually observed against a process I started myself**. They are not
CI-enforced and will not run unattended; only rows 1–5, 8 and 9 are reproducible from a
clean checkout without a running agent.

Rows 8–9 were added after an audit prompted by a "results not reproducible" report:
`npm ci` is the exact command in acceptance criterion 1 and had not been run before, and
the solution build had never been run with the new supervisor project registered.

Reproduce with:

```bash
cd src/hercules-studio && npm install && npm run typecheck && npm test && npm run build && npm run test:e2e
dotnet test tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj
```

Row 6 needs the agent running; row 7 needs the supervisor managing it
(`dotnet run --project src/hercules-supervisor`).

## Acceptance criteria (from the approved plan)

| # | Criterion | Status |
|---|---|---|
| 1 | `npm ci && npm run build` passes; `dist/` opens in a browser | ✅ rows 3, 6 |
| 2 | typecheck 0 errors; tests green | ✅ rows 1, 2, 4, 5 |
| 3 | No `electron` / `better-sqlite3` / `electron-vite` imports in `renderer/` | ✅ grep clean; absent from built bundle |
| 4 | `window.studioAPI` absent; typing via `platform/capabilities.ts` | ✅ absent from built bundle |
| 5 | Contribute key never in `localStorage` / `sessionStorage` / logs | ✅ asserted by E2E tests 3, 4, 12 |
| 6 | Agent serves `/ui` + SSE; SPA same-origin | ✅ row 6; SSE verified separately (`/api/code/run`) |
| 7 | Supervisor survives the restart protocol and writes logs | ✅ row 7 |
| 8 | Hotkeys, i18n en/ru, light/dark themes work in browser | ✅ E2E test 2 (`translations resolve`) |
| 9 | First 3 `hercules-web` panels ported | ⚠️ **superseded** — owner decision 2026-10-06: not ported, `hercules-web` deprecated |
| 10 | README + ADR updated; ADR-0001 superseded | ✅ |

Criterion 9 is the only one not met as originally written. It was closed by explicit
owner decision, recorded in [ADR-0009](adr/0009-web-first-studio.md) and
[`src/hercules-web/DEPRECATED.md`](../../src/hercules-web/DEPRECATED.md).

## Out of scope — ⚠️ NOT YET CONFIRMED BY THE OWNER

> **This section's scope decision was auto-adopted when a question timed out, not chosen
> by the owner.** The owner has not answered it. Until they do, the 243 open Stage 0–9
> items remain arguably in scope, and **criterion 9 / goal completion is not settled.**

Decisions that *were* explicitly confirmed by the owner (2026-10-06, questionnaire answered
by the user, not a timeout default):

- chat stays request/response;
- `hercules-web` is deprecated rather than ported — this closes criterion 9.

Decision that was **not** confirmed — auto-selected from the recommended default on timeout:

- whether Stage 0–9's functional requirements belong to this goal or a separate project.

**If the owner confirms the separate-project reading**, the migration is the whole
deliverable and this goal closes. **If not**, the 243 open Stage 0–9 items are in scope and
this goal is not complete.

For reference, the state of that work either way:

- Task files show `stage_00` 50 done / 102 open and stages 01–09 at **0 done / 243 open**.
  Those checkboxes are **stale** — this migration delivered substantial parts of Stage 1–7
  without ticking them — so the counts are an upper bound on unfinished work, not a precise
  remainder.
- Real gaps: Monaco and history/diff, Vue Flow, **MCP CRUD**, context distillation +
  Postgres, the consensus engine, the BPMN designer, and the OpenAPI codegen pipeline
  (openapi-typescript / Orval). Authoritative list:
  [`src/hercules-web/DEPRECATED.md`](../../src/hercules-web/DEPRECATED.md).
- Studio's `sdk/client.ts` is therefore still hand-written; the codegen pipeline that
  would replace it is part of that next project, not this migration.

Do not read "migration complete" as "product roadmap complete".

## Known caveats

- Running the agent rewrites `Hercules.WebApi/agent-card.json` (`generatedAt` only) — a
  pre-existing behaviour that dirties the tree on every start. It is not gitignored.
- `ChatView` renders the reply in one piece. Streaming was investigated and declined
  (see ADR-0009); the typing effect is presentational and labelled as such.
- `WorkflowView`'s Run action is a backend stub (task_105) and is labelled as one in the UI.