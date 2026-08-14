# Stage 8 — BPMN Workflow Designer

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 3-4 недели
**Dependencies (backend):** task_104 (workflow-server), task_105 (graph model + executor), task_106 (DelegatedTask persistence), task_107 (parent/child tasks), task_108 (checkpoint persistence)
**Dependencies (Studio):** Stage 7

## Goal

Визуальное проектирование multi-agent workflow с BPMN-подобной оркестрацией. Две фазы: MVP (Studio-orchestrator) → Production (hercules-workflow-server).

## Phase 8a — MVP (Studio-orchestrator)

### 8.1 — Workflow graph model

- [ ] `shared/workflow-model.ts`:
  - `WorkflowGraph` — nodes + edges + metadata
  - Node types: `StartNode`, `ServiceTaskNode`, `ConditionalNode`, `UserTaskNode`, `EndNode`
  - `ServiceTaskNode`: { agentId (or connectionId), intent, payload, timeoutMs }
  - `ConditionalNode`: { expression (simple: `result.field == "value"`), branches: {true: nextNodeId, false: nextNodeId} }
  - `UserTaskNode`: { question, inputType (text/file/approval/choice), choices[] }
  - `WorkflowExecution` — { id, graphId, status, nodeStates: Map<nodeId, NodeState>, startedAt, completedAt }
  - `NodeState` — { status: pending|running|completed|failed|waiting, result, error, startedAt, completedAt }

### 8.2 — Workflow Designer (Vue Flow)

- [ ] `views/WorkflowDesigner.vue`:
  - Vue Flow canvas with BPMN-like nodes
  - Palette (left sidebar): Start, Service Task, Conditional, User Task, End (drag to canvas)
  - Node properties panel (right sidebar, on select):
    - ServiceTask: select agent (from connections), intent (capability), payload (JSON editor), timeout
    - Conditional: expression editor, branch labels
    - UserTask: question, inputType, choices (if choice)
  - Edges: connect nodes (drag from port to port)
  - Validation: graph must have exactly one Start and one End, all nodes reachable
  - Save → SQLite `workflows` table (graph_json)
  - "New" → empty canvas
  - "Open" → list saved workflows → load
  - "Delete" → remove from SQLite

### 8.3 — Studio executor

- [ ] `electron/main/workflow-executor.ts` (main process for durability):
  - `execute(graph, connections)` — runs workflow
  - Algorithm:
    1. current = Start node
    2. loop:
       - ServiceTask: `POST /api/mesh/intent` to target agent → wait response → store result
       - Conditional: evaluate expression on previous result → next branch
       - UserTask: send IPC to renderer → show form → wait user input → continue
       - End: done, return results
    3. Update node states → IPC → renderer (live UI update)
    4. Handle errors: node failed → show error, offer retry/skip/abort
  - Timeout per node, global timeout
  - Cancel: user can abort execution
- [ ] Persistence: save execution state to SQLite (for resume after Studio restart — basic)

### 8.4 — Live monitoring

- [ ] `components/workflow/ExecutionMonitor.vue`:
  - Vue Flow canvas with node states (color: pending=gray, running=blue, completed=green, failed=red, waiting=amber)
  - Click node → details: result, error, duration
  - "Cancel" button → abort execution
  - "Retry failed" button → re-run failed node
  - Auto-scroll to active node

### 8.5 — User Task form

- [ ] `components/workflow/UserTaskForm.vue`:
  - Modal/dialog when UserTask node is reached
  - Shows question from node
  - Input based on inputType:
    - text: textarea
    - file: file picker (IPC to main process)
    - approval: "Approve" / "Reject" buttons
    - choice: radio buttons from choices[]
  - Submit → continue workflow

### 8.6 — Workflow templates

- [ ] 5 templates + 1-2 corporate:
  1. **Code review flow** — agent A analyzes code → agent B reviews → conditional (pass/fail) → end
  2. **Research flow** — agent A researches topic → agent B summarizes → end
  3. **Multi-step refactor** — agent A plans → agent B implements → agent C tests → conditional
  4. **Approval flow** — agent A drafts → UserTask (approve) → agent B executes → end
  5. **Data pipeline** — agent A fetches → agent B transforms → agent C analyzes → end
  6. **Corporate: CRM sync** — webhook trigger → agent A validates → UserTask (confirm) → agent B syncs → end
  7. **Corporate: ECM document** — agent A classifies → agent B extracts → UserTask (review) → agent C files → end
- [ ] `components/workflow/TemplateGallery.vue` — grid of template cards → create from template

### 8.7 — Workflow store

- [ ] `stores/workflow.ts`:
  - `graphs: WorkflowGraph[]` — saved workflows
  - `active: WorkflowGraph | null` — editing
  - `execution: WorkflowExecution | null` — running
  - `list()`, `load(id)`, `save(graph)`, `delete(id)`
  - `run(graph)`, `cancel()`, `retry(nodeId)`
  - `templates: WorkflowTemplate[]` — hardcoded

### 8.8 — Sidebar: Workflow activity

- [ ] Sidebar for "Workflow" activity:
  - "New workflow" button
  - Saved workflows list (click → open in designer)
  - Templates section
  - Running executions (if any)

## Phase 8b — Production (hercules-workflow-server)

### 8.9 — Workflow-server connection

- [ ] Connection Manager (Stage 1) extended: support `workflow-server` as source type
  - Separate connection: baseUrl + API key (workflow-server's own key, not agent key)
  - Health: `GET /api/workflows/health` (workflow-server endpoint)
- [ ] `sdk/endpoints/workflows.ts`:
  - `listDefinitions()`, `saveDefinition(graph)`, `runDefinition(id, input?)`, `listExecutions(id)`, `getExecution(eid)`
  - Auth: API key header (workflow-server key)

### 8.10 — Workflow-server backend (task_104-108)

- [ ] `hercules-workflow-server` (.NET, separate project):
  - REST API: `/api/workflows/*` (CRUD, run, monitor)
  - Auth: clientId/clientSecret (simplified, no JWT)
  - Executor: reads graph, calls `/api/mesh/intent` to agents, persists state
  - Triggers: webhook (`POST /api/workflows/triggers/webhook/{token}`), cron (Quartz/Hangfire)
  - Persistence: SQLite or Postgres (workflow definitions, executions, node states)
  - DelegatedTask: linked to workflow execution (parent/child)
  - Monitoring: `GET /api/workflows/executions/{eid}` → node states
  - SSE for live updates (future, long-poll for MVP)

### 8.11 — Production monitoring

- [ ] `components/workflow/ProductionMonitor.vue`:
  - Long-poll `GET /api/workflows/executions/{eid}` (or SSE) → live node states
  - Same UI as MVP monitor (8.4) but data from workflow-server
  - Webhook trigger: show webhook URL per workflow (`/api/workflows/triggers/webhook/{token}`)
  - Cron trigger: show cron expression, next run time
  - Execution history: list past executions, click → view (read-only)

### 8.12 — Full BPMN (production)

- [ ] Extended node types (beyond MVP):
  - ParallelGateway: split into parallel branches, join (wait all)
  - ExclusiveGateway: one path based on condition
  - InclusiveGateway: multiple paths
  - TimerEvent: wait N seconds
  - ErrorEvent: catch error, redirect
  - SubProcess: nested workflow (call activity)
- [ ] Vue Flow: custom nodes for each BPMN type, BPMN-like shapes

### 8.13 — Tests

- [ ] Unit: workflow graph validation, executor logic (mock agents), expression evaluation
- [ ] E2E (MVP): design workflow → run → see live states → user task → complete
- [ ] E2E (Production): save to workflow-server → run → monitor → webhook trigger

## Acceptance criteria

### MVP (8a)
- [ ] Workflow Designer: Vue Flow canvas, palette (5 node types), drag, properties panel
- [ ] Graph validation: one Start, one End, all reachable
- [ ] Save/load/delete workflows (SQLite)
- [ ] Studio executor: runs linear + conditional workflows, live node states
- [ ] User Task: form appears, submit → continue
- [ ] Templates: 7 templates (5 general + 2 corporate), create from template
- [ ] Cancel/retry execution
- [ ] `npm run build` + tests pass

### Production (8b)
- [ ] Workflow-server connection in Connection Manager
- [ ] Save/run/monitor via workflow-server API
- [ ] Webhook trigger: URL shown, works on POST
- [ ] Cron trigger: expression set, schedules
- [ ] Full BPMN: parallel/exclusive/inclusive gateways, timer/error events, sub-processes
- [ ] Execution history
- [ ] Long-poll monitoring (SSE future)

## Scope / Likely files

- `shared/workflow-model.ts`
- `renderer/src/views/WorkflowDesigner.vue`
- `renderer/src/components/workflow/` (ExecutionMonitor, UserTaskForm, TemplateGallery, ProductionMonitor, node types)
- `renderer/src/stores/workflow.ts`
- `electron/main/workflow-executor.ts`
- `src/workflow-server/` (new .NET project — backend, task_104-108)

## Dependencies

- **Backend:** task_104 (workflow-server), task_105 (graph model + executor), task_106 (DelegatedTask persistence), task_107 (parent/child), task_108 (checkpoints)
- **Studio:** Stage 7

## Risks / Rollback

- **BPMN scope:** full BPMN is large. Mitigation: MVP = linear + conditional only, full BPMN in 8b.
- **Studio executor durability:** Studio close = workflow lost. Mitigation: persist state to SQLite, resume on restart (basic).
- **Workflow-server complexity:** separate .NET service = more deployment. Mitigation: architecture in Stage 0, detailed impl in 8b.

## Links

- Epic: [../README.md](../README.md)
- Stage 7: [stage_07_consensus.md](stage_07_consensus.md)
- ADR-0008 (workflow-server): [../adr/0008-workflow-server-architecture.md](../adr/0008-workflow-server-architecture.md)