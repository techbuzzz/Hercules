# ⛔ hercules-web is deprecated

**Status:** Deprecated as of 2026-10-06. Do not add features here.

Hercules Studio (Vue 3 SPA, `src/hercules-studio`) is now the primary UI. See
[ADR-0009 — Web-first Hercules Studio](../../docs/EPIC_Hercules_Studio/adr/0009-web-first-studio.md).

**Owner decision (2026-10-06):** panels are **not** ported. Porting all 19 would have
recreated the two-frontends problem the migration exists to remove, since several map
onto the same agent endpoints Studio already uses.

## Coverage — what Studio actually replaces

Studio calls exactly these endpoints today (verified by inspecting the views):

| Studio view | Agent endpoints used |
|---|---|
| Agents | `/agent.manifest.json` (A2A discovery), `/api/health`, CheckIn/CheckOut |
| Chat | `/api/chat` |
| Config | `/api/config` GET/PATCH, `/api/system/restart` |
| Skills | `/api/skills`, `/api/code/run` + SSE |
| Mesh | `/api/mesh/agents`, `/api/mesh/health`, `/api/mesh/denials` |
| Tools | `/api/tools` + enable/disable |
| Consensus | `/api/approvals/pending`, `/api/escalations/pending` + approve/deny |
| Workflow | workflow-server `/api/workflows` (list/get/delete) |

### Panels replaced

| hercules-web panel | Studio equivalent |
|---|---|
| `ChatBox` | Chat |
| `ConfigEditor` | Config |
| `SkillCard` | Skills |
| `DiscoveryPanel` | Agents (A2A discovery) |
| `EscalationPanel` | Consensus (approvals + escalations) |

### Partially replaced — read this before assuming parity

| Panel | What Studio covers | What is **missing** |
|---|---|---|
| `MeshDashboard` | agent list, health scores, circuit state | topology and traffic (agent returns `unknown` for both) |
| `MeshObservabilityPanel` | per-agent health + policy denials | latency percentiles, trend history |
| `AgentCardPanel` | discovery list + name/endpoint | full A2A card detail (skills, capabilities, trust metadata) |
| `CapabilityRegistryPanel` | agent rows in Mesh | the capability matrix itself |

### **Not** replaced — no Studio equivalent exists

| Panel | Missing capability |
|---|---|
| `SecurityOpsPanel` | security-ops endpoints are unused by any Studio view |
| `TrustAdmissionPanel` | mesh trust admission flow |
| `BackupPanel` | backup listing/restore |
| `RolloutPanel` | rollout management |
| `QuotasPanel` | quota inspection and editing |
| `SloPanel` | SLO definitions and status |
| `MeshProfilePanel`, `MeshRouterPanel` | mesh profile / router configuration |
| `ProfileEditor` | agent profile editing |
| MCP management | `listMcpServers()` exists in the SDK client but **no view calls it** |

Workflow execution is also not yet available: `/api/workflows/{id}/run` is a **backend stub**
(task_105). The Studio Workflow view lists, inspects and deletes definitions, and labels the
Run action as a stub rather than pretending it works.

## If you need one of the missing panels

Add it to **Studio** — do not revive this project. The gap list above is the backlog; the
agent endpoints for most of them already exist and are typed in
`src/hercules-studio/renderer/src/sdk/client.ts`.

## Still reachable

The app still builds (`npm run build` here) and its dev server runs on port **4322** —
which is why Studio's dev server was moved to 4330.