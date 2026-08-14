# Stage 7 — Консилиум

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 2-3 недели
**Dependencies (backend):** нет (Studio orchestrates parallel /api/chat)
**Dependencies (Studio):** Stage 2, Stage 1

## Goal

Параллельный chat с N агентами + агрегация (LLM-judge + Manual pick). Notifications для async operations. Voting + Merge = next gen (future).

## Tasks

### 7.1 — Consensus store

- [ ] `stores/consensus.ts`:
  - `selectedAgents: Connection[]` — agents selected for consensus
  - `prompt: string` — shared prompt
  - `responses: Map<connectionId, ChatResponseDto>` — per-agent responses
  - `aggregationMode: "manual" | "llm-judge"` — current mode
  - `judgeAgent: Connection | null` — selected judge for LLM-judge
  - `aggregatedResult: string | null` — final result
  - `status: "idle" | "querying" | "aggregating" | "done"` — pipeline status
  - `send()` — `Promise.all(selectedAgents.map(a => client.chat(prompt)))` parallel
  - `aggregateManual(pickedConnectionId)` — user picks best
  - `aggregateLlmJudge(judgeAgent)` — send all responses to judge, get best
  - `reset()`

### 7.2 — Consensus view

- [ ] `views/Consensus.vue`:
  - **Top:** agent selector (multi-select from connections, chips), prompt input, "Send to all" button
  - **Middle:** response columns (one per selected agent), side-by-side:
    - Column header: agent name, status (querying/done/error)
    - Column body: response markdown, badges (mode, confidence, skill, provider)
    - "Best" button per column (for Manual pick)
  - **Bottom:** aggregation panel:
    - Mode selector: Manual | LLM-judge
    - If LLM-judge: judge agent selector (one of selected or separate)
    - "Aggregate" button → runs aggregation
    - Result: aggregated answer in markdown
  - Empty state: "Select agents and enter a prompt to start a consensus"

### 7.3 — Agent selector

- [ ] `components/consensus/AgentSelector.vue`:
  - Multi-select from connections (chips with remove)
  - Filter: online only (default)
  - Show: agent name, status, capabilities count
  - Min 2 agents required for consensus
  - Max N agents (configurable, default 10)

### 7.4 — Response columns

- [ ] `components/consensus/ResponseColumn.vue`:
  - Props: connection, response, status
  - Markdown rendering (markdown-it + highlight.js)
  - Badges: mode, confidence, skill, provider
  - "Best" button (Manual pick mode) → highlights column, sets as aggregated
  - Error state: red border, error message
  - Loading state: spinner, "Querying {agent name}..."

### 7.5 — LLM-judge aggregation

- [ ] `components/consensus/LlmJudgePanel.vue`:
  - Select judge agent (dropdown from connections, can be one of selected or external)
  - "Run judge" → send all responses + prompt to judge agent:
    ```
    POST /api/chat to judge agent
    body: { message: "You are a judge. Select the best answer to: '{original prompt}'. Here are {N} responses: [1: {response1}, 2: {response2}, ...]. Return JSON: {best_index, rationale}" }
    ```
  - Parse judge response: best_index, rationale
  - Highlight winning column, show rationale
  - If judge fails → fallback to Manual pick

### 7.6 — Manual pick

- [ ] User clicks "Best" on a response column:
  - Column highlighted with gold border
  - Aggregated result = that response
  - "Confirm" button → set as final

### 7.7 — Notifications (async operations)

- [ ] `stores/notifications.ts` (расширить):
  - Native notifications via Electron `Notification` API (IPC to main process)
  - Triggers:
    - Consensus query complete: "Consensus complete: {N} responses received"
    - Long-running chat (if response > 5s): "Agent {name} responded"
    - Workflow step complete (Stage 8): "Workflow step {name} done"
    - Escalation pending: "Escalation requires approval"
  - In-app notification center (bell icon in StatusBar)
  - Settings: enable/disable native notifications

### 7.8 — Consensus history

- [ ] Save consensus sessions to SQLite (new table or extend chat_history):
  - id, prompt, selectedAgents (JSON), responses (JSON), aggregationMode, result, createdAt
  - "Load past consensus" → list → click → view (read-only)

### 7.9 — Sidebar: Consensus activity

- [ ] Sidebar for "Consensus" activity:
  - New consensus button
  - Past consensus list (click → view)
  - Quick agent select (favorites?)

### 7.10 — Tests

- [ ] Unit: consensus store send/aggregate, LLM-judge parsing, manual pick
- [ ] E2E: select 3 agents → send prompt → see 3 columns → pick best → confirm

## Acceptance criteria

- [ ] Agent selector: multi-select chips, min 2, max 10, online filter
- [ ] Send: parallel `POST /api/chat` to all selected → responses in columns
- [ ] Response columns: markdown, badges, "Best" button, loading/error states
- [ ] LLM-judge: select judge → run → parse best_index → highlight winner + rationale
- [ ] Manual pick: click "Best" → highlight → confirm
- [ ] Aggregated result shown in bottom panel
- [ ] Notifications: native + in-app, consensus complete, long chat, escalation
- [ ] Consensus history: save/load past sessions
- [ ] `npm run build` + tests pass

## Scope / Likely files

- `renderer/src/views/Consensus.vue`
- `renderer/src/components/consensus/` (AgentSelector, ResponseColumn, LlmJudgePanel)
- `renderer/src/stores/consensus.ts`, `stores/notifications.ts`

## Dependencies

- **Backend:** нет (Studio orchestrates parallel /api/chat)
- **Studio:** Stage 1 (connections), Stage 2 (chat)

## Risks / Rollback

- **Parallel request timeout:** some agents may be slow. Mitigation: per-agent timeout, show partial results.
- **LLM-judge parsing:** judge may not return valid JSON. Mitigation: fallback regex parse, then manual pick.
- **Voting + Merge:** future features, not in MVP. Documented in roadmap.

## Links

- Epic: [../README.md](../README.md)
- Stage 1: [stage_01_agent_scanner.md](stage_01_agent_scanner.md)
- Stage 2: [stage_02_chat_skills.md](stage_02_chat_skills.md)
- Stage 8: [stage_08_workflow.md](stage_08_workflow.md) (workflow = multi-step consensus)