# Hercules Studio migration — verification record

Date: **2026-10-06**. Machine: Windows, `node v22.22.0`, `.NET 10.0.12`.
Branch: `tasks/studio-ui`. Nothing was committed; this records state of the working tree.

This file exists so the migration's verification is **inspectable**, not merely asserted.

## What was verified, and how

Every command below was run in this working tree on the date above.

| # | Command | Result |
|---|---|---|
| 1 | `npm run typecheck` (studio) | 0 errors (`vue-tsc --noEmit -p tsconfig.web.json`) |
| 2 | `npm test` (studio) | **27 passed / 0 failed** (2 files: 24 platform, 3 OpenAPI contract) |
| 3 | `npm run build` (studio) | succeeded — 8 lazy view chunks |
| 4 | `npm run test:e2e` (studio) | **12 passed** (26.1s), Chromium |
| 5 | `dotnet test tests/Hercules.Agent.Tests` | **2233 passed / 0 failed** (2m55s) |
| 6 | Agent runtime, live HTTP | `/ui/` **200**, 1545 bytes, app root div present · session exchange **200**, role `contribute`, 43-char token · `/agent.manifest.json` 401 without a key (expected) · `/api/config` audited as a contribute session — **no plaintext secret in the response** |
| 7 | Supervisor restart cycle, live | pid 24384 → 14804, `state: running`, `crashCount: 0`, restart flag `pending: false` |
| 10 | Studio unit tests | superseded by row 17 — see the current figure there |
| 11 | Studio E2E | **30 passed** (1m02s), Chromium — hermeticity guard, fake-clock auto-refresh, dashboard panels, 6-action context menu, router sort/filter coverage |
| 12 | `dotnet build src/agent/Hercules.slnx` | succeeded, 0 warnings, 0 errors — solution includes `Hercules.Supervisor` |
| 13 | OpenAPI codegen (Stage 0) | `npm run generate:api` → `openapi.d.ts`; contract test proves every `sdk/client.ts` endpoint is documented |
| 14 | `dotnet test tests/Hercules.Agent.Tests` (final) | **2276 passed / 0 failed** (2m56s), and green under the exact `-c Release --no-build` command CI runs |
| 15 | `npm run lint` (studio) | clean, 67 files |
| 16 | OpenAPI generation determinism | identical SHA-256 across an incremental and a `--no-incremental` build, so the CI drift job cannot flake |
| 17 | Studio unit tests | **97 passed / 0 failed** (24 platform + 3 contract + 24 view-registry labels + 13 prompt diff + 9 skill package + 9 judge protocol + 6 notifications + 9 web.test) |

Rows 10–13 supersede the earlier studio counts, which predate the contract test. Rows 14–17
cover Stage 5b, 6.3, 7 and the CI baseline and are the current figures.

Rows 6–7 are **manually observed against a process I started myself** — a running agent
serves the `/ui` bundle, exchanges sessions, and can be restarted through the supervisor
protocol. They are *not* CI-enforced and will not run unattended, because CI has no agent
process to attach to.

Everything else here reproduces from a clean checkout with no agent running, and as of the
Stage 9 CI work is now **enforced on every push** rather than merely reproducible:

| Row | Enforced by |
|---|---|
| 1 typecheck, 10 unit, 15 lint | `studio` job |
| 3 build, 11 E2E | `studio` job (Playwright stubs the agent with `page.route`, so the suite is hermetic and needs no live agent) |
| 5 tests, 12 solution build | `agent` job |
| 13 codegen, 16 determinism | `openapi` job — regenerates `openapi.json` and fails on drift |

Rows 8–9 were added after an audit prompted by a "results not reproducible" report:
`npm ci` is the exact command in acceptance criterion 1 and had not been run before, and
the solution build had never been run with the new supervisor project registered.

## Stage 0 — API codegen (in progress)

`openapi.json` was found **stale**: `/api/code/run` and `/api/studio/session` were live on
the server but absent from the document, because the artifact is opt-in
(`-p:OpenApiGenerateDocumentOnBuild=true`) and nothing had regenerated it.

Done so far:

- Document regenerated → **214 paths**.
- `npm run generate:api` → `renderer/src/sdk/openapi.d.ts` (213 KB of types).
- `renderer/src/sdk/client.contract.test.ts` — fails if any endpoint the hand-written
  client calls is missing from the agent document, and asserts the document is not stale.
  It is the guard that would have caught the drift above. Proven functional: it failed
  twice before passing, catching the stale document and two normaliser bugs.
- `ToolDto` / `ToolsResponse` are now **derived from the document**
  (`components["schemas"]["ToolSummaryDto"]`), not hand-written. The generated shape
  matched the hand-written one on all 16 fields.

### Blocker found while wiring the generated types

The document declared **no response schemas at all** for the endpoints Studio consumes —
`GET /api/tools` returned `{"description":"OK"}` with no `content`. Endpoints return
anonymous objects, which `AddOpenApi` cannot describe, so full client codegen is
impossible until the agent names its response shapes.

Fixed for the tool registry as the reference pattern
(`Hercules.WebApi/Contracts/ToolRegistryDtos.cs` + `.Produces<ToolsListResponseDto>(200)`
on the endpoint):

- Non-nullable DTO members must use `required`. Without it the emitter omits them from
  the schema's `required` array, every field generates as optional (`string | undefined`),
  and the generated client stops reflecting what the agent actually serialises. This was
  caught only because the generated type disagreed with the hand-written one.
- `int32` fields generate as `number | string` (OpenAPI permits string-encoded integers);
  the wire value is a number, so consumers coerce.

### The bug codegen caught

Deriving `SkillDto` from the document instead of hand-writing it surfaced a **real defect**:
the agent returns `phraseReceivers` (`WebApiAdapter.SkillDto`), but the hand-written type
declared `triggers`. `SkillsView` read `skill.triggers`, which the API never sends — so the
trigger line rendered empty against the real agent. The E2E fixture carried the same
invented shape, which is why the suite stayed green. Both are fixed.

`Trigger`/`Triggers` is legacy **input** on `CreateSkillRequest`, not a response field.

### Progress — response schema coverage is now 11/11

Every endpoint Studio consumes is described by the agent document:

`/api/tools` · `/api/skills` · `/api/skills/{id}` · `/api/chat` · `/api/config` ·
`/api/approvals/pending` · `/api/escalations/pending` · `/api/mesh/health` ·
`/api/mesh/denials` · `/api/studio/session` · `/api/code/run`

DTOs now derived in `sdk/types.ts`: `SkillDto`, `SkillDetailDto`, `ChatResponseDto`,
`AgentConfigDto`, `CodeRunStatusDto`, `ToolDto`, `ToolsResponse`, `ApprovalDto`,
`ApprovalsResponse`, `EscalationDto`, `EscalationsResponse`, `MeshHealthEntryDto`,
`MeshHealthDto`, `MeshPolicyDenialsDto`.

Only four shapes remain hand-written, each with the documented reason:
`ConfigDto` (config is an open-ended dictionary), `MeshAgentDto` (`/api/mesh/agents`
returns bare registry records), `MeshDashboardDto` (`/api/mesh/dashboard` unannotated),
`StatsDto` (`/api/stats` unannotated).

Notes for whoever regenerates:
- `int32`/`double` generate as `number | string` (OpenAPI permits string-encoded
  numbers), so views coerce via `Number(...)` — `ToolsView.failureCount`,
  `SkillsView.successRate`, `MeshView.num`.
- Regenerating the document and the types is a two-step dance:
  `dotnet build src/agent/Hercules.WebApi -p:OpenApiGenerateDocumentOnBuild=true`
  then `npm run generate:api` in `src/hercules-studio`.

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

## Scope — CONFIRMED by the owner (2026-10-06)

**The functional roadmap (`Stage 0–9`) IS in scope for this goal.** The owner answered this
explicitly (questionnaire returned `explicitUserConfirmation: true`). An earlier version of
this section claimed the opposite — that Stage 0–9 was a separate project — because a
questionnaire had auto-selected the recommended default on timeout. **That was wrong and is
withdrawn.** The migration was never the complete deliverable; it is the foundation.

Explicitly confirmed owner decisions (2026-10-06):

- chat stays request/response;
- `hercules-web` is deprecated rather than ported — this closes criterion 9;
- **the Stage 0–9 roadmap is in scope** — see below for what remains.

Therefore **9 of 10 migration criteria are met.** The Stage 0–9 roadmap is in scope and is
covered stage by stage below; one item there — **task_105**, the workflow executor — is
blocked on an owner decision rather than on effort.

### What remains

The task-file checkboxes are **not a usable measure of remaining work** and this section
previously leaned on them. The raw count is **345 unticked boxes** across `stage_00`–`stage_09`
(`node scripts/count-stage-tasks.cjs` re-derives it), but **stages 1–9 have zero boxes ticked**
even though a great deal of them has shipped. An earlier version of this document quoted
"243 open items" and listed OpenAPI codegen, Monaco, MCP CRUD and Vue Flow as the top
priorities — **all four have since been delivered**. That claim is corrected here so it cannot
be cited as outstanding work.

| Stage | Delivered | Genuinely open |
|---|---|---|
| 0 — codegen | `openapi-typescript` pipeline, contract test, generated `openapi.d.ts` | Orval — skipped deliberately, see note |
| 1 — agent scanner | superseded by A2A discovery | — |
| 2 — chat & skills | Monaco editor, prompt history + Restore, **revision diff view** | — |
| 3 — skill push | cross-agent push, MCP pre-check, per-agent results, 5 templates, .skillpkg builder, **dangerous-code pre-check** | — |
| 4 — mesh explorer | canvas, node details, context menu, shared memory, circuit breakers | — |
| 5 — tools & MCP | tool toggle, MCP list/add/edit/delete, **pre-push MCP status check** | — |
| 6 — config & restart | raw editor, LLM, roles, mesh inspector, quotas, context, restart, session-store editor (staged, restart-gated, partial-backend warning) | — |
| 7 — consensus | parallel fan-out, structured LLM-judge with rationale, manual pick, notifications, **persisted history** | voting + merge (not in the task spec) |
| 8 — workflow | graph model, validation, editor, in-place update fix | **task_105 — executor + typed `WorkflowGraph`** |
| 9 — packaging | CI (GitHub Actions), lint baseline | packaging itself cancelled (PWA) |

Authoritative per-panel detail: [`src/hercules-web/DEPRECATED.md`](../../src/hercules-web/DEPRECATED.md).

**Fixed at source, not just here.** Each of the ten task files carried `**Status:** pending`
and unticked boxes, so opening one implied nothing had been built — which is precisely how
"243 open items" kept being quoted as outstanding work. Each file now opens with a status
banner stating that the checkboxes are not a progress report, and its `**Status:**` field
reflects reality (`scripts/stamp-stage-status.cjs`, `scripts/set-stage-status.cjs`).

## Stage 2 — Monaco (partially done)

**Done:** the skill prompt textarea is now a Monaco editor (`components/common/CodeEditor.vue`),
used by `SkillsView`. Two implementation notes worth keeping:

- `import * as monaco from "monaco-editor"` pulls `esm/vs/index.js`, which statically
  registers **every** language contribution. That emitted ~9.5 MB of workers
  (`ts.worker` alone was 7 MB, plus a 19 MB sourcemap) for editors that only hold
  Markdown or JSON, and made the build take 43 s. Using
  `monaco-editor/editor/editor.api` plus the Markdown tokeniser definition drops it to
  two workers (`editor.worker` 276 kB, `json.worker` 408 kB) and an 11.6 s build.
  Add language-specific workers back only when a view genuinely needs diagnostics.
- monaco-editor 0.57's exports map is `"./*" -> "./esm/vs/*.js"`, so import specifiers
  must **omit** the `esm/vs/` prefix; including it resolves to a doubled path.

**Prompt history/diff: done.** The agent now records every prompt it is about to overwrite
(`Hercules.WebApi/Skills/SkillPromptHistoryStore.cs`, append-only, 50 revisions per skill,
atomic tmp+move writes, corrupt files discarded rather than fatal).
Wired into `PUT /api/skills/{id}` and `POST /api/skills/{id}/improve`; exposed as
`GET /api/skills/{id}/prompt-history`. Recording failures never fail the edit — history is
an audit aid, not a dependency.

Studio's skill editor gained a collapsible history list with **Restore**, which writes an
old prompt back through the normal save path rather than bypassing it.

Edits were previously destructive: the agent kept quality and eval history but discarded
prompt history, so there was nothing to diff against.

## Stage 4 — Vue Flow workflow graph: blocked on the backend

`WorkflowView` renders `graphJson` as raw JSON. A node-graph view was **not** built,
because the schema it would render does not exist:

`src/workflow-server/Models/WorkflowDefinition.cs` describes `GraphJson` as an
*"arbitrary JSON graph"*, and the typed model it will eventually be parsed into —
`WorkflowGraph` with `StartNode` / `ServiceTaskNode` / `ConditionalNode` /
`ParallelGateway` — is referenced only in a doc comment as task_105 work. **No such type
exists in the codebase.**

Rendering it now would mean inventing a node schema and guessing at the mapping. The same
pattern as Stage 2b applies: do the backend first (typed `WorkflowGraph`), then the view.

## Stage 6 — status per subtask

| # | Subtask | Status |
|---|---|---|
| 6.1 | Config view | ✅ `ConfigView.vue` (raw JSON + merge patch) |
| 6.2 | LLM providers editor | ✅ `LlmView.vue` — provider, fallback chain, models, health probe. **Secret-free**: `/api/llm/config` deliberately excludes API keys |
| 6.3 | Roles editor | ✅ **implemented as an agent capability.** See "Stage 6.3 — roles editor" below |
| 6.4 | Mesh/A2A endpoints editor | 🔶 **partial.** Delivered as a routing *inspector* (`MeshView`): `/api/mesh/router/routes` answers "who would serve capability X and why" with scores, trust and circuit state. There is no write-back surface to edit, so it is labelled an inspector |
| 6.5 | Quotas editor | ✅ `ConfigView` quotas section — live usage from `/api/quotas`, limits patched via config. `IQuotaService` is read-only, so no separate write endpoint was invented |
| 6.6 | Context budget + distillation | ✅ `ContextView.vue` |
| 6.7 | Raw config editor | ✅ same view as 6.1 |
| 6.8 | Restart agent | ✅ system-role gated; supervisor performs the kill |
| 6.9 | MCP hot-reload | ✅ MCP section in `ToolsView` |
| 6.10 | C# file-based test-run | ✅ sandbox run + SSE in `SkillsView` |
| 6.12 | Postgres centralized storage | ⛔ **needs a backend change.** `Storage:SessionStore` is read from `IConfiguration` in `Program.cs`, **not** from `AppConfig`, so `/api/config` cannot reach it. Surfacing it means changing where the agent reads it, and the change only takes effect on restart — switching backends live risks data loss. Left for a deliberate decision |
| 6.13 | Sidebar Config activity | ✅ `ConfigView` registered in the view registry |

## Stage 5b — MCP add / edit / remove

The earlier note that this was impossible ("no add/remove endpoint") was wrong about the
constraint: `McpClientService` implements `IConfigReload`, so writing `mcp.servers` through
`PATCH /api/config` already makes the agent connect and disconnect on its own. No new write
endpoint was needed — only a read surface that carries enough to edit.

| Check | How | Result |
|---|---|---|
| List endpoint carries editable config | `GET /api/mcp/servers` → `McpServerSummaryDto.Config` (`McpServerDefinitionDto`) | ✅ config is the source of truth; runtime state joined by name |
| Disabled server is visible and editable | status `Disabled` via new `McpServerHealthStatus.Disabled` | ✅ `GET` reports it with `enabled: false` |
| Editor round-trips through merge-patch | `ToolsView` writes `{ mcp: { servers: [...] } }` — arrays are replaced wholesale, so the full list is always sent | ✅ asserted in `studio.spec.ts` ("tools view edits MCP servers") |
| Writes settle before status is shown | after a patch the view awaits `POST /api/mcp/servers/reload`, because `IConfigReload.Reload` is fire-and-forget | ✅ E2E asserts reload count goes 1 → 2 |
| Validation | name required & unique, stdio requires a command, http/sse requires a parseable absolute URL | ✅ |

**Defect found and fixed:** `Enabled` gated in-process hosting (`McpServerHost`) but **not**
client connections (`McpClientService`) — a server switched off in config was still dialled
out, and its tools registered. Disabled servers are now excluded from the desired set, which
also routes an already-connected server through the disconnect path, so flipping the flag off
really disconnects it. Covered by 4 new tests in `McpClientServiceTests`.

## Stage 6.3 — roles editor

Roles live in `WebApi:ApiKeys` / `keys.json`, outside `AppConfig`, so `/api/config` cannot
reach them. Rather than surface a read-only list, this adds the missing write capability:

| Endpoint | Purpose | Guard |
|---|---|---|
| `GET /api/auth/keys` | metadata only — fingerprint, role, description, label | system role |
| `POST /api/auth/keys` | create; agent generates a key when none is supplied | system role |
| `PATCH /api/auth/keys/{fingerprint}` | change role / description | system role |
| `DELETE /api/auth/keys/{fingerprint}` | remove | system role |

**Secrets.** The raw key is never returned by the list endpoint and appears only once, in
`createdKey.generatedKey` at creation. Studio addresses keys by `ApiKeyStore.Fingerprint` —
12 hex chars of SHA-256 — so a browser that has never seen a key can still manage it. The
`label` field is derived only from the known family prefix (`hc_sys_`, `hc_contrib_`), so it
cannot leak token material. E2E asserts the generated plaintext appears once and disappears.

**Why the middleware changed.** `ApiKeyMiddleware` snapshotted its key table in the
constructor, which lives for the whole process — a role edit would have been reported as
saved but only applied after a restart, which is a silent security failure. The key set now
lives in `ApiKeyStore` behind a swappable immutable table and is resolved per request.
`ApiKeyMiddlewareTests` pins this: a key promoted through the editor is accepted at system
role on the *same* middleware instance, and a removed key is rejected on the next request.

**Lockout guards.** The middleware treats an empty key set as "authentication disabled" and
lets every request through, so any mutation that would leave zero keys — or zero system
keys — is refused with an explanatory 400 rather than accepted.

**Session revocation.** A session carries the role captured at exchange. Sessions now record
the minting key's fingerprint, and deleting or demoting a key revokes the sessions it issued
immediately, instead of letting them keep the old role until TTL. Sessions issued before this
change carry no fingerprint and are deliberately left alone, since they may belong to any key.

## Stage 9 — CI

`.github/workflows/ci.yml`, three jobs:

- **Agent** — `dotnet restore/build/test` on `src/agent/Hercules.slnx`, trx uploaded always.
- **Studio** — typecheck → lint → unit → build → Playwright E2E on Chromium.
- **OpenAPI freshness** — regenerates `openapi.json` and fails if it differs from the
  committed file. This closes the loop on a failure the contract test documents: during the
  migration the committed document was found stale, with live routes missing from it.

Generation was verified deterministic (identical SHA-256 across an incremental and a
`--no-incremental` build) before adding the drift check, so the job cannot flake.

**Lint baseline.** There was no Biome config, so `biome check .` ran on defaults and failed
with 64+ findings — mostly formatter churn (tabs vs. the codebase's 2-space indent) and
import reordering, neither of which is a defect. `biome.json` now turns off formatting and
import ordering, and scopes `noUnusedVariables` off for `.vue` only, because Biome 1.9 does
not see `<script setup>` bindings referenced from the template — it flagged a working
`submit()` in `ReauthDialog.vue`. The `lint` script is `biome lint .` rather than `check`.

The 8 genuine style findings that remained were fixed rather than suppressed, including two
that were worth it: `ConsensusView` now passes the captured client into its `act()` helper
instead of re-reading `client.value!` in a closure, and `client.ts` no longer assigns inside
the SSE loop condition.

## Stage 7 — Consensus (multi-agent fan-out)

| Check | How | Result |
|---|---|---|
| Two agents can be queried at once | `connections.clientFor(id)` builds a per-connection client; the store previously exposed only the active one | ✅ cached per connection, token read per request so a re-exchange is picked up |
| Fan-out is parallel | all agents dispatched in one `Promise.all` of independent settles | ✅ |
| **A failing agent does not lose the others** | each settle patches its own row; a bare `Promise.all` would discard every success on one rejection | ✅ asserted in `studio.spec.ts` |
| Minimum of two enforced and explained | store guards `canSend`; `AgentSelector` shows "needs at least two agents" | ✅ asserted |
| Manual aggregation picks a column | `aggregateManual(id)` copies that agent's answer into the result panel | ✅ asserted |
| LLM-judge is an ordinary chat round | judge prompt built in the store, sent via the same `/api/chat`; no judge-specific endpoint invented | ✅ |
| Nothing persisted | a consensus round is transient; writing agent replies into browser storage would leak answer content | ✅ |

**Deliberate deviations from the task file**, both recorded rather than silently taken:

- **`markdown-it` + `highlight.js` were not added.** Neither is a dependency and
  `ChatView` does not render markdown either; rendering it only here would make the same
  agent reply look different in two views. Responses use `whitespace-pre-wrap`, matching
  `ChatView`. Markdown, if wanted, should be added across both views at once.
- **Notifications were not built.** They depend on Stage 9 packaging decisions that were
  cancelled with the desktop build; `platform.notify` already exists for the Web
  Notification path.

**Naming collision resolved.** The approvals/escalations view was registered as
`consensus` before Stage 7 existed. It is now `decisions` (`DecisionsView.vue`,
`activity.decisions`), so "consensus" means only the multi-agent feature — which is what
the roadmap means by it. The existing E2E spec was renamed accordingly.

## Stage 8 — workflow authoring

| Check | How | Result |
|---|---|---|
| An existing definition can be updated | `SaveWorkflowRequest.Id` added and passed through; store upserts on conflict | ✅ 2 new tests in `SqliteWorkflowDefinitionStoreTests` |
| An unknown id does not silently create | controller returns 404 before upserting, so a typo cannot masquerade as an update | ✅ |
| A new definition still gets its own id | omitting `id` mints a fresh one | ✅ asserted |
| Structural validation before save | `validateGraph` — one StartNode, unique ids, edges resolve, no self-loops, service tasks need an intent and valid JSON payload, conditionals need an expression | ✅ 13 unit tests |
| Create vs. update is distinguishable in the client | E2E asserts a create sends **no** id and an edit sends the existing one | ✅ |
| Saving re-reads the definition | the view reloads after save rather than trusting the local draft | ✅ |
| Execution is not faked | `run` remains a labelled 501 (task_105) | ✅ |

**Two bugs found and fixed while building this:**

- **Editing a workflow silently created a duplicate.** `SaveWorkflowRequest` had no
  `Id`, so every save minted a new one despite the endpoint being documented as
  "create or update". Fixed at the model, not worked around in the client.
- **Double-encoded request body.** `platform.workflows.save` passed an already
  `JSON.stringify`-ed body to `workflowFetch`, which serialises `init.body` itself — the
  server received a JSON string where it expected an object and would have answered
  `graphJson is required`. Caught by the E2E test, not by typecheck.

**Deliberate deviations:** the editor is a structured node/edge form, not the Stage 4
Vue Flow canvas, because the canvas depends on a typed `WorkflowGraph` that task_105 owns.
The model is plain data, so a canvas can render the same graph later without reshaping
storage. `parseGraph` is deliberately faithful rather than repairing — a stored graph with
no nodes is reported as such so validation complains, instead of being backfilled with a
StartNode that would make a broken definition look editable.

## Security finding — `GET /api/config` was returning live credentials

Found while scoping Stage 6.12, and fixed rather than deferred.

`ConfigController.ToDictionary` serialised the entire `AppConfig` into the response body.
`AppConfig` embeds real secrets — four LLM provider API keys (`llm.*.apiKey`), the Postgres
session-store connection string (`storage.sessionStore.connectionString`, which carries a
password), a Telegram bot token, and a signing key. The endpoint is authenticated but not
role-gated, so **any** connected Studio operator — including `contribute` role — could read
all of them, and the raw-JSON Config view displayed them in the browser.

This also contradicted the agent's own design: `/api/llm/config` deliberately excludes API
keys, and Studio's LLM view is documented as "secret-free", but `/api/config` handed out the
same values through the side door.

| Check | How | Result |
|---|---|---|
| Secrets are gone from the response | `ConfigRedactor.Redact` replaces secret-named values with `__hercules_redacted__` | ✅ asserted against a real `AppConfig` |
| Every secret-bearing path is covered | test builds a populated `AppConfig` (4 LLM keys + connection string + bot token) and asserts none appear | ✅ |
| Non-secret config survives | provider name and `sessionStore.provider` still readable, or the endpoint stops being useful | ✅ |
| No over-redaction | exact-name matching, so `distributedKeyPrefix` / `idempotencyKey` / `secretReferencePrefix` stay visible | ✅ asserted |
| Empty ≠ hidden | an unset key stays `""`; a configured one shows the marker | ✅ |
| **Round trip does not destroy secrets** | `PATCH /api/config` strips redacted properties before merging, so Studio editing masked config cannot write the mask over a real API key | ✅ asserted |

Matching is by **property name**, not an enumerated path list, so a secret added to
`AppConfig` later is redacted by default instead of silently becoming readable — the failure
mode is a hidden value rather than a leaked one.

Verified that nothing in Studio depended on reading secrets back: `ConfigView` reads only
the `quotas` section, and the raw editor just renders what the server sends.

**Verified against a live agent — and that found a real gap.** Starting the agent and
auditing an actual `GET /api/config` response as a *contribute* session showed the original
exact-name matcher missing compound names that a real config genuinely contains:
`backup.passphrase`, `nats.authToken`, `notifications.smtpPassword`, `edge.enrolmentToken` —
none is literally `password` or `token`, so all four would have leaked once set. The matcher
now matches secret tokens as **substrings**, with a small explicit allow-list of non-secrets
that contain them (`secretReferencePrefix`, `distributedKeyPrefix`, `idempotencyKey`,
`tokenBudget`, `tokenTtlSeconds`), because under-redaction is a security defect and
over-redaction is cosmetic.

A second live audit after the change shows **no plaintext secret anywhere in the response** —
every secret-named property is empty, null, or the marker. The same run confirmed the point
of the substring approach: `redis.connectionString` and `postgres.connectionString` were
masked automatically, neither of which any unit test enumerated.

## Stage 6.12 — session store backend

| Check | How | Result |
|---|---|---|
| Current provider is visible, the secret is not | section reads `/api/config`, which returns `connectionString` as the redaction marker | ✅ asserted |
| **Leaving the secret alone is expressible** | the field is omitted from the patch unless the operator typed one, so the existing value survives | ✅ asserted both ways |
| Explicit new value is sent | typing a connection string does include it | ✅ |
| Save trusts the server, not the draft | the section re-reads after saving rather than assuming its own state | ✅ asserted |
| System-role only | section is gated on `isSystem`, like the roles editor | ✅ |
| Restart semantics are stated | `ISessionStore` is chosen by conditional DI registration at startup (`Program.cs:595`), so a change cannot apply live | ✅ stated in the UI |
| Backend maturity is stated | `task_103.md:16` records `PostgresSessionStore` as partial — durable tasks, checkpoints, escalations, sandbox runs and skill evaluations throw `NotImplementedException` | ✅ stated in the UI |

**Deliberately not a live toggle.** Two reasons, both surfaced in the UI rather than hidden:
the DI registration is chosen at startup, and the postgres backend is only partially
implemented. An editor that made switching frictionless would let an operator enable a
store that throws on five feature families. Staging the value and requiring an explicit
restart is the safe half; the live-switch half is the part that needs an owner decision.

## Stage 4 — correction: the recorded blocker was wrong

Earlier notes recorded Stage 4 as blocked because a typed `WorkflowGraph`
(`StartNode`/`ServiceTaskNode`/`ConditionalNode`/`ParallelGateway`) "does not exist".
**That is a misdiagnosis and is withdrawn.** Two separate things were conflated:

- `Stage 4` is the **Mesh Explorer** — a Vue Flow canvas of the mesh topology
  (`docs/EPIC_Hercules_Studio/tasks/stage_04_mesh_explorer.md`). It never needed
  `WorkflowGraph`.
- The typed `WorkflowGraph` belongs to **Stage 8 / task_105** — the workflow
  *executor* schema that `src/workflow-server/Models/WorkflowDefinition.cs:7-9` says
  task_105 will define. Stage 8's authoring model already follows the shape documented in
  `tasks/stage_08_workflow.md:18-23`.

**Stage 4's real precondition was a bug, not a missing type.** `/api/mesh/agents` answers
`{ count, agents }`, but `getMeshAgents()` was declared `Promise<unknown[]>`, and MeshView
guarded the result with `Array.isArray(...) ? ... : []`. Against a live agent the guard
failed on every response, so the agent list rendered **permanently empty**. The E2E stub had
been returning a bare array — i.e. it encoded the wrong shape and could not catch this.

| Fix | Detail |
|---|---|
| `getMeshAgents()` typed as the envelope | prevents the mismatch being reintroduced; the endpoint returns an anonymous object so there is no generated schema |
| `unwrapAgents()` in `MeshView` | accepts the envelope and a bare array, so neither shape silently empties the view |
| E2E stub corrected | now returns `{count, agents}`, the real shape — the assertions that previously passed now actually exercise the unwrap |

Also worth recording: **Stage 4 needs no backend work.** `GET /api/mesh/dashboard` already
returns typed `MeshDashboardDto`, containing `Topology: MeshTopologyDto`
(`MeshAgentDto[]` with healthScore, latencyMs, qualityScore, trustLevel, capabilities) and
`Health: MeshHealthDto` — which is exactly the node source the canvas needs (colour by
health, size by capability count, label by displayName/agentId/endpoint).

Remaining for Stage 4 is the canvas itself (`@vue-flow/core` is not yet a dependency),
the node details panel and the context menu.

## Stage 4 — mesh topology canvas

| Check | How | Result |
|---|---|---|
| Canvas renders typed topology | `MeshCanvas.vue` over `MeshDashboardDto.topology` | ✅ asserted in `studio.spec.ts` |
| Node encoding is explained, not implicit | legend states colour = health band, size = capability count | ✅ asserted |
| Health bands match the health table | ≥0.7 healthy, ≥0.4 degraded, else unhealthy, rendered with per-band counts | ✅ |
| Node details on click | `NodeDetailsPanel.vue` — endpoint, health, quality, latency, trust, last seen, capabilities | ✅ |
| Deterministic layout | ring, radius scaled by capability count (clamped), no physics | ✅ |
| Auto-refresh every 30s | E2E installs a fake clock and fast-forwards 35s rather than waiting 30 real seconds | ✅ |
| Polling stops when the view unmounts | the interval is cleared on unmount; leaving Mesh and advancing two minutes issues no further request | ✅ |
| No polling of a hidden tab | refresh is skipped while `document.visibilityState === "hidden"`; returning to the tab refreshes immediately | ✅ |

**4.7's dashboard panels were specified and missing.** `loadTopology` was already fetching
`GET /api/mesh/dashboard` and discarding everything but `topology` — the payload also carries
`traffic`, `skillHeatmap` and `evalSummary`, all generated and typed. `MeshDashboardPanels.vue`
now renders them: six traffic counters, a usage-weighted skill heatmap, and the eval summary
with recent runs. No new endpoints; this was data already on the wire and thrown away.

| Context menu has all six 4.4 actions | details / touch / **register peer** / **cleanup stale** / remove / reset-circuit | ✅ asserted, 6 items |
| Registry-wide actions confirm | cleanup is destructive and prompts first | ✅ asserted |
| Register peer takes a manifest URL | endpoint takes a manifest *body*, so the manifest is fetched first and failures are reported | ✅ |

The context menu was 4 of the 6 actions 4.4 specifies; "Register peer" and "Cleanup stale
agents" were missing. Both are registry-wide rather than node-scoped, so they emit without an
agent id and are dispatched separately from the per-node actions.

**4.5's router explorer is now complete.** It previously searched by capability only and
showed the router's ranking unmodified. Added: phrase search via
`/api/mesh/capabilities/search?phrase=`, column sorting (composite score, health, latency,
cost) and three filters (trust passed, healthy, circuit not open).

Sorting and filtering are applied **client-side over the router's own ranking** — the
endpoint returns every candidate with its scores, so re-ordering is presentation only and
cannot change which peers the router would actually pick.

Writing the test surfaced three things, none of which were the feature:
- The stub glob `**/api/mesh/routes**` does not match `?capability=deploy`; `**/api/mesh/**`
  does. A stub that silently never fires looks exactly like a broken feature.
- The row's text is `#1 C-untrusted…`, not a standalone name, so `exact: true` locators miss.
- Two fixture agents tied at health 0.99, making the expected order depend on tie-breaking.
  Distinct health values make the test express intent instead of incidental order.
| Re-fits when peers join | `watch` on agent count calls `fitView` | ✅ |
| `double` fields coerced once | generator emits `number \| string`; normalised via a `score()` helper rather than at each call site | ✅ |
| Dependency cost | `@vue-flow/core@1.48.2` added; `npm audit` reports **zero** Vue Flow advisories (13 pre-existing, all vitest/vue toolchain) | ✅ |
| Nav labels resolve | new `view-registry.test.ts` asserts every `labelKey` resolves in en and ru | ✅ |

**Layout is a ring, not force-directed.** The registry exposes no edge list, so there is
nothing for a force layout to optimise, and a deterministic ring reads more clearly than a
simulation that reshuffles on every 30s refresh. The canvas says so on screen: *"Nodes only.
The registry does not expose trust/delegation relations as edges yet."* Drawing edges the
backend cannot supply would have been a fiction.

**Two more bugs this work surfaced:**

- **Two nav items rendered raw i18n keys.** `activity.context` and `activity.llm` were
  absent from both `en.json` and `ru.json`, so vue-i18n fell back to printing the key —
  the sidebar literally showed `activity.context`. vue-i18n's silent fallback is what made
  this invisible to the existing "no component renders empty" test. Fixed, and
  `view-registry.test.ts` now asserts every registry `labelKey` resolves in both locales.
- **`MeshDashboardDto` was hand-written with `topology: unknown`.** The dashboard endpoint
  carried no response schema. It is annotated now, so `MeshDashboardDto`,
  `MeshTopologyDto` and `MeshAgentDto` are generated from the document and the hand-written
  stubs are gone.

Stage 4.6 and 4.7 are now also built, with **zero backend work** — both endpoints already
existed (`GET /api/mesh/circuits` + `POST /api/mesh/circuits/{id}/reset`, and
`GET|POST|DELETE /api/mesh/shared-memory` + `POST /api/mesh/shared-memory/sync`):

| Panel | Behaviour | Evidence |
|---|---|---|
| `CircuitPanel.vue` | rows per peer with state colour, open count, per-peer reset | ✅ reset asserted as a real POST |
| `SharedMemoryPanel.vue` | list + filter, publish (category/content), sync from peers, delete with confirm | ✅ list + audience asserted |

**Another instance of the shape bug, caught before it shipped.** `GET /api/mesh/circuits`
answers a **map** (`{ agentId: state }`, from `CircuitBreaker.GetAllStates()`), not an array —
the same class of defect as `/api/mesh/agents`. An `Array.isArray` check there would have
produced a permanently empty circuit panel. The SDK now normalises map-or-array into rows,
so the mistake cannot be repeated at the call site.

Deletion of a shared fact **confirms first and says why**: peers that already received the
fact keep their copy, so "delete" here removes the local record rather than retracting
anything. The dialog states that instead of implying a broadcast undo.

**Stage 4.4 — node context menu** is now built, completing Stage 4. Right-clicking a
canvas node opens a menu with *View details* / *Touch* / *Reset circuit breaker* /
*Remove from registry*, each mapping to a real endpoint
(`GET /api/mesh/agents/{id}`, `POST /api/mesh/agents/{id}/touch`,
`POST /api/mesh/circuits/{id}/reset`, `DELETE /api/mesh/agents/{id}`).

| Property | How |
|---|---|
| Actions are real calls, not local toggles | E2E asserts touch and circuit-reset each produce their POST |
| Only the destructive action confirms | *Remove* confirms and explains the peer disappears until it re-registers |
| The menu cannot latch open | closes on Escape, on any outside click, and on choosing an item; listeners removed on unmount |
| Touch devices don't break it | Vue Flow's `node-context-menu` carries `MouseTouchEvent`, so the menu coordinates are guarded rather than assumed |
| Union event names dispatch correctly | `emit(kind, id)` does not narrow to a specific overload, so `act()` switches per-kind |

## Stage 2 — skill prompt diff view

Closes the last Stage 2 item. `restore` and `improve` already wrote a revision before
overwriting a prompt, so the data for a diff existed; only the comparison was missing — **no
backend work**.

| Check | How | Result |
|---|---|---|
| LCS-based, not positional | unchanged runs stay `context` instead of collapsing into delete+insert pairs | ✅ 9 unit tests |
| CRLF normalised | a Windows-authored prompt does not diff as fully replaced | ✅ |
| Identical revisions say so | explicit notice, not an empty panel that looks like "nothing loaded" | ✅ asserted |
| Size guard | prompts over 2000 lines degrade to "fully replaced" rather than allocating an n×m table | ✅ |
| Collapsed context | long unchanged runs fold to `⋯ n unchanged line(s)` | ✅ |
| Diffed against the **draft**, not the saved skill | the useful question is "what would restoring this change?" | ✅ |
| Per-revision accessible labels | `Diff version N` / `Restore version N` instead of several identical buttons | ✅ |

Hand-rolled rather than pulling in a diff dependency: the bundle already carries Monaco,
and this stays a pure function that unit-tests without a DOM.

## Stage 3 + Stage 5 pre-check — cross-agent skill push

Almost all of Stage 3's backend already existed (`POST /api/skills`, `PUT /api/skills/{id}`,
`POST /api/skills/import` with the agent's own replace|skip|rename, and `GET /api/skills/{id}/export`).
The missing piece was Studio-only: `PushSkillDialog.vue` plus `importSkill()` for multipart.

| Check | How | Result |
|---|---|---|
| Push reaches several agents in parallel | targets settle independently, one `Promise.all` of independent settles | ✅ asserted: 1 PUT accepted, 1 refused |
| **One failure does not lose the others** | per-agent results rendered inline, not swallowed behind an all-clear | ✅ asserted — the refusal text is visible |
| Summary is honest | "pushed to 1 of 2 agents" rather than a success toast | ✅ asserted |
| MCP pre-check is per target | each target's `/api/mcp/servers` resolved before the push | ✅ asserted |
| Pre-check states what it is | explicitly a *status* check, not a dependency analysis | ✅ in the UI copy |

**The pre-check is deliberately not a dependency check.** `SkillDto` carries no tool list, so
there is no honest way to say "this skill needs server X". What it reports is each target's
configured MCP servers and how many are unhealthy — enough to know whether MCP-backed tools
will work once the skill lands, without inventing a dependency graph the API does not expose.

**"New" is a fresh record on the target, not a blank editor.** `CreateSkillRequest.Prompt` is
required server-side, so both update and new push the skill currently being viewed; only
import works without one. The mode buttons reflect that.

Two things the type checker caught that are worth recording: Vue templates cannot carry
`as` casts, so the panel uses a precomputed view model rather than casting inline; and
`emit(kind, id)` does not narrow to a specific overload for a union of event names.

## Stage 3.4 — creating a skill from Studio

**Studio had no create path at all.** The editor only opened existing skills, so
`POST /api/skills` — present in the agent and in the SDK client — was never reachable from
the UI. Stage 3's premise ("Создание навыков в Studio") was therefore unmet even though the
endpoint existed.

| Check | How | Result |
|---|---|---|
| Five roadmap templates offered | HTTP call, code execution, A2A delegate, file-based .NET, blank | ✅ asserted |
| Creating lands in the editor | the new skill is selected, not left on the list | ✅ asserted |
| Modern trigger field used | templates declare `phraseReceivers`; the legacy single `trigger` is filled as a fallback | ✅ |
| `.NET` example is honest | the template ships a prompt and snippet, not compiled code — Studio authors skills, the agent's sandbox runs them | ✅ in the template |

`createSkill` gained an optional `phraseReceivers` because the backend prefers the plural
field over the legacy `trigger`, and a template-created skill should not differ in shape from
one edited in place.

## Stage 3.3 — .skillpkg package builder

Packages the **current editor state** into a `.skillpkg` and downloads it, so an
unsaved draft can be packaged without saving it first.

| Check | How | Result |
|---|---|---|
| Layout matches the spec | entries under a top-level directory whose name equals `meta.id` — what an importer rejects otherwise | ✅ 9 unit tests |
| Meta is snake_case | `phrase_receivers`, not `phraseReceivers`, which the deserializer would silently drop | ✅ asserted both ways |
| Required entries present | `skill.meta.json`, `skill.prompt.md`, `skill.description.md` | ✅ |
| Optional C# included only when present | `code.cs` added for a file-based app, omitted otherwise | ✅ |
| File naming per spec | `{id}-v{version}.skillpkg` | ✅ asserted end-to-end via the browser download |
| CRC-32 correct | standard IEEE vector `0xcbf43926` for `"123456789"` | ✅ |
| Deterministic | same inputs produce identical bytes, so a package's hash is verifiable | ✅ |
| Downloaded bytes are a real ZIP | E2E reads the download stream and checks the local-header signature | ✅ |

**Store-only ZIP, written by hand.** A skill package is a handful of small text files, so
DEFLATE would cost a dependency for no real saving, and "no compression" keeps the archive
auditable by eye. `.NET`'s `ZipArchive` — which the agent's importer uses — reads store
entries natively.

The implementation was checked against `docs/skill-package-spec.md` rather than assumed,
which is what surfaced the directory-name-equals-id rule and the snake_case requirement.

**A note on the optional `code.cs` entry.** `SkillPackager` has no `code.cs` handling, and the
package spec's own file-reference table (Section 8) does not list `code.cs` at all — the
`.cs` mention exists only in the Stage 3 task file ("optional .cs files"). So the entry is
supported and tested but is **not part of the current package format**, rather than being a
supported feature that went unpopulated. Whether a skill should own executable source at
all is a design question (it is what Stage 6.10's "C# files" tab and the file-based-app
skill type assume), and it interacts with task_105's executor work.

## Stage 3.5 / 6.10 — dangerous-code pre-check

`DangerousCodeScanner.Scan(code, SandboxOptions)` existed and was enforced inside the
executors, but it had **no HTTP surface** — a scan-only call was impossible, so Studio could
not warn before a run. Added `POST /api/code/scan`, using the same scanner and the same
DI-registered `SandboxOptions` the executors use, so the reported verdict is exactly what
execution would enforce.

| Check | How | Result |
|---|---|---|
| One ruleset, not two | endpoint calls the agent's scanner; no Studio-side regex copy | ✅ by construction |
| Denial blocks the run | `POST /api/code/run` is not reached | ✅ asserted (`runs === 0`) |
| Finding is located | line number rendered alongside the reason | ✅ asserted |
| Override is explicit, not a retry | requires ticking the override before anything runs | ✅ asserted |
| **Override is scoped to the reviewed code** | editing the snippet invalidates it; re-running the same snippet keeps it | ✅ |
| A failed scan is not a pass | a scan error is reported as a denial | ✅ |

The override scoping was a real bug caught by the test: clearing it on every `run()` call
made the checkbox undo itself on the second click, so an agreed override was unusable.

**Where this lives, and why not in the push dialog.** The task file asks for the pre-check
"before push", but there is no skill-owned code to scan. Verified rather than assumed:
`SkillPackager` has no `code.cs` handling, the package spec's file table (Section 8) does not
list `code.cs`, `SkillDetailDto` carries no `code` field, and `code.cs` is only a *sandbox
session* artifact (`DotnetFileBasedExecutor.cs:75`). C# reaches the sandbox only as a one-off
snippet via `POST /api/code/run`. `ARCHITECTURE.md:168` claimed `code.cs` was stored per
skill — corrected, since that claim had been cited twice before being checked.

So the pre-check is wired to the sandbox **run** flow, where the operator's code actually
exists. A pre-check at push time would have had to invent a code source.

## Stage 7 — notifications for async operations

`platform.notify` already existed but nothing used it. Wired to the two operations that
genuinely outlive a view: the **consensus round** (parallel fan-out across agents) and the
**sandbox run** (SSE, up to two minutes).

| Check | How | Result |
|---|---|---|
| Silent while the page is visible | a toast is already the right feedback; a system popup would be noise | ✅ asserted |
| Permission only requested while visible | a hidden page cannot present a prompt the user can answer | ✅ asserted both directions |
| Unsupported platform is a no-op | `platform.notify.supported()` false → nothing happens | ✅ asserted |
| Refused permission stays silent | no repeated prompts, no error path | ✅ asserted |

**The test caught my code contradicting its own comment.** The first version requested
permission whenever it was `default`, including from a background event, while the comment
above it said it never should. The behaviour now matches: a hidden page with an undecided
permission skips the round, and the prompt is only ever raised when someone is looking.

## Stage 7.5 — LLM-judge, corrected to the specified contract

The task file (`stage_07_consensus.md:71-82`) specifies a **structured** judge: return
`{best_index, rationale}`, highlight the winning column, show the rationale, and fall back to
Manual if the judge fails. The first implementation asked for the best answer *verbatim*,
which loses both the rationale and any way to know which column won — a deviation from the
spec that only showed up by re-reading the task file.

| Check | How | Result |
|---|---|---|
| Judge is asked for the structured contract | prompt contains `best_index` and the valid `1..N` range | ✅ asserted |
| JSON parsed out of real model output | fenced ``` ```json ```, embedded in prose, camelCase, string indices | ✅ 9 unit tests |
| Out-of-range index rejected, not clamped | `0`, `-1`, `9` for N=3 → no verdict | ✅ asserted |
| Winner highlighted | `pickedConnectionId` drives the existing highlight | ✅ |
| Rationale displayed | rendered under the aggregated result | ✅ asserted |
| Unusable judge output falls back to Manual | no verdict → nothing set, round stays hand-pickable | ✅ asserted |
| Hand-pick supersedes a judge verdict | manual pick clears the rationale so the two cannot disagree | ✅ |

Parsing is deliberately forgiving — balanced-brace extraction, not `JSON.parse` of the whole
reply — because models fence and prefix JSON often enough that a strict parse fails in
practice. It is still strict about *meaning*: a nonsensical index yields no verdict, because a
guessed winner presented as a verdict is worse than none.

## Stage 7.8 — consensus history

A consensus round is the record of what several agents said and which answer was chosen.
`SkillRunStore` — the closest existing store — is in-memory with a 15-minute TTL, which is
right for SSE replay and wrong here: losing the history on restart is exactly when someone
wants to review why a decision came out as it did. So this persists agent-side.

| Check | How | Result |
|---|---|---|
| Survives a restart | re-open the store over the same `DataRoot` | ✅ asserted |
| Newest first | list ordering | ✅ asserted |
| Re-saving an id replaces | no duplicates | ✅ asserted |
| Bounded on disk | capped at 50, oldest pruned | ✅ asserted |
| Corrupt file starts empty | history is not worth failing startup over | ✅ asserted |
| Answers kept verbatim | no summarising — the point is to review what was said | ✅ asserted |
| Round is recorded when it finishes, not on page close | E2E asserts the POST after completion | ✅ |
| Detail is read-only | Studio renders history, never edits it | ✅ |

Seven backend tests plus an E2E. Stored under `{DataRoot}/consensus/sessions.json`, written
via temp-file-then-move so a crash mid-write cannot truncate the history.

## Known caveats

### `noUncheckedIndexedAccess` is off — measured, not assumed

`strict: true` is set, so Stage 9's "TS strict" is met. Enabling
`noUncheckedIndexedAccess` on top produces **49 type errors across 12 files**:

| File | Errors |
|---|---|
| `skills/promptDiff.ts` | 15 — 2-D LCS table indexing |
| `stores/connections.ts` | 9 |
| `platform/web.ts` | 4 |
| `views/LlmView.vue`, `views/EmptyState.vue` | 5 |
| `stores/consensus.ts`, `skills/skillPackage.ts`, `workflow/graph.ts`, `GraphEditor.vue`, `SkillsView.vue` | 7 |
| `platform/web.test.ts`, `sdk/client.contract.test.ts` | 9 |

Most of these accesses are already guarded at runtime — the judge parser rejects an
out-of-range index, and the scan panel builds parallel arrays together — but the compiler
cannot prove either.

Measured and reverted rather than half-applied: enabling a compiler flag that surfaces 49
errors and fixing only some would leave the tree worse than before. Recorded in
`stage_09_packaging.md` as its own reviewable change.

### 314 broken documentation links, now zero

Found by writing a link checker rather than reading files one at a time — the same lesson
as the stale-frontend sweep above.

Files under `docs/roadmap/tasks/completed/` were moved into that directory at some point
and their relative links were never updated, so `../backlog.md` resolved to
`tasks/backlog.md` instead of `roadmap/backlog.md`. The same breakage existed in
`tasks/` proper. **314 links across ~104 files were dead** — the agent's own task history
was largely unreadable.

`scripts/fix-task-links.cjs` repairs them conservatively: a link is rewritten only when
its current target does not exist *and* an alternative does, so nothing is invented.
`scripts/check-doc-links.cjs` now verifies all 841 relative links resolve and is
re-runnable.

Four links could not be mechanically repaired and were handled explicitly rather than
silently dropped:

| Link | Reality |
|---|---|
| `PLAN-v2.md` (EPIC README + task_101) | the document does not exist anywhere; replaced with plain text saying so |
| `docs/index.md` → `demo` | no demo video was ever produced; repointed to `assets/demo/demo-script.md`, which does exist |
| `ROADMAP-*.md` → `../../discussions`, `../../issues` | these are GitHub UI routes, not repo paths; converted to absolute URLs |
| `backlog.md` task_092 | claimed `done`, but the file has 9 unticked boxes — the eight genuinely-complete siblings were moved to `completed/`, this one was **not**, and its row now says `partial` |

That last one is the substantive find: the backlog was overstating a task as complete.

**The same overstatement repeats seven more times.** Having found task_092, I wrote
`scripts/check-backlog-status.cjs` to check every row rather than assume it was isolated.
117 rows, 117 task files read — **seven more `done` rows had unticked boxes**, two of them
badly overstated:

| Task | Unticked | What the boxes actually were |
|---|---|---|
| 109 | **29** | Predate task_111, which is what really produced the OpenAPI document |
| 104 | 18 | Checklist *parent headings* (`REST API:`, `Auth:`, `Executor:`) with sub-items ticked |
| 71, 87 | 6, 4 | Cross-referenced deferrals to task_073/075/077 and tracked follow-ups |
| 74, 83, 85 | 1 each | `Skip=` integration test, a deferred multi-node layer, a deferred `ILogger` migration |

So none of these were hidden missing work — they were bare `done` standing in for
"done, with deferrals I wrote down in the task file". Rows now say which, so the claim is
checkable without opening five files. The checker treats a *qualified* `done (…)` as honest
and still rejects a bare `done` with open boxes.

### Both top-level READMEs documented the architecture we deleted

The web-first migration removed Electron and deprecated the Astro frontend
(`src/hercules-web`, ADR-0009 criterion 9). Both `README.md` and `README-RU.md` still
instructed readers to run it — `cd src/hercules-web && npm run dev` on port **4321**, in two
terminals alongside the agent, and to publish the Astro bundle separately with a static file
server. Anyone following either README would have got a completely different system from the
one that exists.

Worth noting the port was wrong twice over: `4321` was never the real dev port, and the
agent's actual CORS whitelist is `4322/4330/8421` (`Program.cs:976`), where `4330` is Studio's
Vite dev server.

Updated: quick start, project structure (Studio added, `hercules-web` marked deprecated with a
pointer to its replacement map), publishing (one artefact, served at `/ui`), the CORS
references, the frontend stack list, and the whole "Web Interface" section in both languages.
The only surviving `hercules-web` / Astro mentions are the deliberate DEPRECATED markers.

**The same rot was in eight more files**, found by sweeping rather than checking one at a
time — which is the lesson. `docs/QUICKSTART-EN.md`, `docs/QUICKSTART-RU.md`, `docs/index.md`
(the repo landing page), `docs/CONFIGURATION-EN.md`, `docs/CONFIGURATION-RU.md`,
`docs/ARCHITECTURE-EN.md`, `docs/ARCHITECTURE-RU.md`.

The worst of them was `docs/index.md`, which still told a new visitor to
`cd src/hercules-web && npm install && npm run dev`. The two configuration references also
documented a `hercules-web/.env` with `PUBLIC_API_BASE` / `PUBLIC_API_KEY` that no longer
exists, and gave CORS defaults of `4321`/`3000` — neither of which the agent has ever
allowed (`4322/4330/8421`, `Program.cs:976`).

A repo-wide sweep for `localhost:4321`, `hercules-web/dist`, `npx serve`,
`PUBLIC_API_BASE` and `PUBLIC_API_KEY` now returns nothing outside the intentional
deprecation notices.

### The CI workflow was never executed — now its commands are verified locally

`.github/workflows/ci.yml` was written from knowledge of the local commands and had never
been run. A typo or a broken assumption would only surface on the first push. Verified:

| Check | Result |
|---|---|
| Workflow parses: 3 jobs under `jobs:`, top-level `name`/`on`/`jobs`, no tabs | ✅ `scripts/check-ci-workflow.cjs` |
| Every referenced path exists | ✅ `global.json`, `src/agent/Hercules.slnx`, `src/hercules-studio/package-lock.json` |
| Every `npm run <script>` it invokes exists in `package.json` | ✅ typecheck, lint, test, build, test:e2e |
| `dotnet restore src/agent/Hercules.slnx` | ✅ restores, including the cross-directory `../workflow-server`, `../hercules-supervisor` and test project |
| `dotnet build … -c Release --no-restore` | ✅ 0 warnings, 0 errors |
| `dotnet test … -c Release --no-build` | ✅ 2276 passed, and the `agent-tests.trx` landed where the workflow's upload step expects it |

The last row mattered: `--no-build` would have failed on a test project the solution did
not build, and it does reference `../../tests/Hercules.Agent.Tests`.

`scripts/check-ci-workflow.cjs` is worth keeping — the workflow still has never run on
GitHub, so a local structural check is the only guard available before the first push.

**Line endings for the diff check are now pinned.** The `openapi` job fails on *any*
`git diff`, including line-ending-only churn. Checked before changing anything: the
committed blob is LF and a Linux runner generates LF, so today it is safe — but only
because `core.autocrlf=true` happens to be set on this machine. That is a per-machine Git
setting, not a repo guarantee, so a contributor with `autocrlf=false` would produce a
content-identical file that still fails the job. `.gitattributes` now pins `eol=lf` for
`openapi.json` and the generated `openapi.d.ts`. No bug was fixed here — a fragile
precondition was made explicit.

### The E2E suite was not hermetic — now it is, and it is asserted

`studio.spec.ts` claimed in its header that agent calls are stubbed "so the suite is
hermetic and does not need a running agent". **That claim was false.** `stubAgent` only
stubbed discovery, health and the session exchange; every *view-level* endpoint was stubbed
individually inside whichever test happened to assert on it. So any test that merely visited
a view made a real request to `localhost:8421` — silently swallowed by a `catch`, and on a
machine that happens to have an agent running it would really have persisted a consensus
record.

Fixed properly rather than by narrowing the claim:

- `stubAgent` now stubs every endpoint a view calls on mount — config, skills, tools, MCP
  servers, all five mesh reads, the code scan, API keys and consensus sessions. Tests that
  care register their own stub afterwards, which wins.
- A new test, **"the E2E suite is hermetic — no request escapes to a real agent"**, registers
  a catch-all *first* (Playwright matches the most recently added handler first, so this
  only sees what nothing else handled), visits seven views, and fails listing any `:8421`
  request that escaped. It caught all eleven unstubbed endpoints when written.

Without this guard the suite was one agent-restart away from writing to someone's real
data.

### Provisional decisions — NOT owner-confirmed

A four-question scope questionnaire (`ask_080517719c5352cd0b69536e`, 2026-10-06)
**auto-submitted on timeout**: `responseSource: automatic_timeout`,
`explicitUserConfirmation: false`. It selected all four recommended options.

That is **not** an owner decision, by the same standard ADR-0009 applies to the earlier
timeout selection in this project — so nothing below is treated as confirmed. Reading the
answers honestly, three of them describe what the code already does, and only one is a
provisional decision:

| Question | Auto-selected | Actual state |
|---|---|---|
| Adopt Orval? | No | **Already the case.** Skipped deliberately; drift protection is met without it (see the note above). No change. |
| Should a skill own executable source? | No | **Provisional withdrawal of Stage 6.10.** The reasoning holds independently — `SkillPackager` has no `code.cs` handling, the package spec omits it, `SkillDetailDto` has no `code`. Recorded as provisional pending explicit confirmation. |
| Postgres switch behaviour? | Stage it, require restart, warn the backend is partial | **Already implemented** before the questionnaire was asked. No change; this answer happens to match shipped behaviour, which is why it is not treated as approval for anything new. |
| task_105 executor? | Owner supplies the schema | **Unchanged.** The auto-selection re-states the blocker; it does not resolve it. task_105 remains the one item needing real input. |

**Net effect: no work was authorised by this questionnaire, and none was performed on the
strength of it.** The single provisional outcome is the Stage 6.10 withdrawal, and it needs
an explicit answer to stand.

### Stage 0 — Orval was not adopted, and why

The task file asks for "`openapi-typescript` **+ Orval**. Replace the hand-written
`renderer/src/sdk/client.ts` with a generated, typed client so agent/Studio types cannot
drift." Only the first half was built.

The stated **purpose** — no drift between agent and Studio — is already met and is enforced:

- `openapi-typescript` generates `openapi.d.ts`; DTOs in `sdk/types.ts` are derived from it
  rather than hand-written. That has already caught real defects (`phraseReceivers` vs
  `triggers`).
- `client.contract.test.ts` extracts **every** API path used in `client.ts` and fails if the
  agent does not document it — a guard Orval would not provide, since generating a client
  does not verify hand-written call sites.
- It also asserts the extractor still matches something, so the guard cannot silently rot.
- The CI `openapi` job regenerates the document and fails on drift, closing the loop.

Adopting Orval would replace the call style across **all twelve views** for a stylistic
change that does not improve drift protection. That is a large, review-heavy diff for no
functional gain, so it was deliberately not done unilaterally. It remains the one Stage 0
item and is safe to pick up on request.

- **The agent suite has a growing set of timing-flaky tests under parallel load.** Across
  full runs, three *different* tests have failed one at a time:
  `InProcessMeshStateStoreTests.WatchAsync_NotifiesOnChange`,
  `TransportFaultTests.SendAsync_WithLatency_ReportsLatency` (asserts `LatencyMs >= 50`
  after a 50 ms injected delay), and
  `InProcessTaskQueueTests.VisibilityTimeout_ReEnqueuesTask_WhenNotAcked`. The last one is
  the clearest case: it sleeps 900 ms waiting for a 300 ms visibility timer, then allows a
  200 ms dequeue window — under load the timer fires late and the window closes first. Each
  passes 3/3 in isolation. None of these areas is touched by this work (the changes are MCP
  reload, API-key auth, response DTOs and Studio views), so this is pre-existing suite
  flakiness, but it is a pattern rather than an accident and is worth a separate fix —
  injecting a `TimeProvider` into the mesh queue would remove the wall-clock dependency.
  Do not read a single red full-suite run as a regression from this migration.
- Running the agent rewrites `Hercules.WebApi/agent-card.json` (`generatedAt` only) — a
  pre-existing behaviour that dirties the tree on every start. It is not gitignored.
- `ChatView` renders the reply in one piece. Streaming was investigated and declined
  (see ADR-0009); the typing effect is presentational and labelled as such.
- `WorkflowView`'s Run action is a backend stub (task_105) and is labelled as one in the UI.
- `GET /api/config` was converted from an anonymous return to a named DTO so the document
  can describe it. Verified at runtime: the response is still `{ config, source }` with
  `source: "runtime"` and 50 camelCase config keys — unchanged.