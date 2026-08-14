# Stage 2 — Chat + Skill Editor

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 2-3 недели
**Dependencies (backend):** нет
**Dependencies (Studio):** Stage 0, Stage 1

## Goal

Паритет с hercules-web по чату + полноценный skill editor с Monaco: prompt.md, meta.json (form + raw), description.md, version diff, lifecycle actions, C# basic syntax highlighting, "Test in chat".

## Tasks

### 2.1 — Chat view (один агент)

- [ ] `views/Chat.vue`:
  - Message list: user/assistant, markdown rendering (markdown-it + highlight.js)
  - Input area: textarea, Ctrl+Enter to send
  - `POST /api/chat` через HerculesClient
  - ChatResponseDto: answer, mode, confidence, provider, skill, suggestions
  - Typing effect (опционально, из settings)
  - Mode/confidence/skill badges per message
  - Skill suggestions: proposeSkillForInput, proposeImproveSkillId → clickable links
  - Empty state: "Start a conversation with {agent name}"
- [ ] Chat history persistence:
  - Save to SQLite `chat_history` per connection
  - Load on view open
  - Clear history button

### 2.2 — Sessions browser (базово)

- [ ] `components/chat/SessionsPanel.vue`:
  - List past sessions from backend (`GET /api/stats` interactions)
  - Click → load session messages
  - Basic: date, message count, skill used
  - Full session browser — позже

### 2.3 — Skills view

- [ ] `views/Skills.vue` (sidebar content для Skills activity):
  - Skill list (cards): name, description, version, successRate, totalUses, triggers chips, evalScore
  - Filter/search by name, triggers
  - Deprecated panel (toggle)
  - Lifecycle actions per skill: Edit, Improve (AI), Evaluate, Deprecate, Rollback, Undeprecate
  - "New skill" button → SkillEditor (empty)
  - Rate colors: >=70% emerald, >=40% amber, <40% rose (как hercules-web)

### 2.4 — Skill Editor (Monaco)

- [ ] `views/SkillEditor.vue`:
  - Tabs:
    - **Form** — meta.json fields: name, description, triggers/phraseReceivers (chips list), tools (multi-select), permissions, riskLevel, budget, tags
    - **Prompt** — Monaco editor, language=markdown, skill.prompt.md
    - **Description** — Monaco editor, language=markdown, skill.description.md
    - **C# files** — Monaco editor, language=csharp, basic syntax highlighting (no LSP for MVP). List of .cs files in skill (if file-based app)
    - **Raw JSON** — Monaco editor, language=json, skill.meta.json full
  - Save draft → SQLite `skill_drafts` (autosave every 5s)
  - "Push to agent" button (→ Stage 3 full impl, here basic POST/PUT)
  - "Test in chat" button → opens Chat view with this skill context
  - Version diff: select version → Monaco diff editor (`skill.{id}.v{N}.md` vs current)
  - "Local changes" indicator: draft != agent version → show "Publish" button

### 2.5 — Skills store

- [ ] `stores/skills.ts`:
  - `list: SkillDto[]`
  - `active: SkillDetailDto | null`
  - `drafts: Map<skillId, SkillDraft>` (local)
  - `load()`, `select(id)`, `create()`, `update(id, patch)`, `improve(id)`, `evaluate(id)`, `deprecate(id, reason)`, `rollback(id)`, `undeprecate(id)`
  - `loadDeprecated()`
  - `saveDraft(skillId, draft)`, `loadDraft(skillId)`, `markDraftPushed(skillId)`

### 2.6 — Chat store

- [ ] `stores/chat.ts`:
  - `messages: ChatMessage[]` (per active connection)
  - `sessions: SessionSummary[]`
  - `send(message)` — POST /api/chat, append response
  - `loadHistory(connectionId)` — from SQLite
  - `clearHistory(connectionId)`
  - `loadSessions()` — from backend stats

### 2.7 — Monaco integration

- [ ] `composables/useMonaco.ts`:
  - Lazy-load Monaco only when editor opens
  - Languages: markdown, json, csharp (basic)
  - Dark/light theme based on settings
  - Diff editor mode for version comparison
- [ ] `components/common/MonacoEditor.vue` — wrapper component

### 2.8 — Sidebar: Skills activity

- [ ] Sidebar for "Skills" activity:
  - Skill list (searchable)
  - "New skill" button
  - "Deprecated" toggle
  - Click skill → SkillEditor opens in MainWorkbench tab

### 2.9 — Sidebar: Chat activity

- [ ] Sidebar for "Chat" activity:
  - Sessions list (past sessions)
  - "New conversation" button
  - Click session → load in Chat view

### 2.10 — Tests

- [ ] Unit: Chat store send/load, Skills store CRUD, Monaco wrapper
- [ ] E2E: send message → see response, create skill → see in list, edit skill → push → see updated

## Acceptance criteria

- [ ] Chat: send message → response with badges (mode, confidence, skill), markdown rendered
- [ ] Chat history: persists per connection, loads on reopen
- [ ] Sessions: list of past sessions visible, click loads
- [ ] Skills: list with cards, search, deprecated panel
- [ ] Skill lifecycle: Edit, Improve, Evaluate, Deprecate, Rollback, Undeprecate — all work
- [ ] Skill Editor: 5 tabs (Form, Prompt, Description, C# files, Raw JSON)
- [ ] Form tab: chips list for triggers, multi-select for tools
- [ ] Monaco: markdown/json/csharp syntax highlighting, dark/light theme
- [ ] Draft autosave: edits saved to SQLite every 5s
- [ ] "Local changes" indicator: shows when draft differs from agent
- [ ] Version diff: Monaco diff editor shows v{N} vs current
- [ ] "Test in chat": opens Chat with skill context
- [ ] Typing effect: опционально из settings
- [ ] `npm run build` + tests pass

## Scope / Likely files

- `renderer/src/views/Chat.vue`, `views/Skills.vue`, `views/SkillEditor.vue`
- `renderer/src/components/chat/`, `components/skills/`, `components/common/MonacoEditor.vue`
- `renderer/src/stores/chat.ts`, `stores/skills.ts`
- `renderer/src/composables/useMonaco.ts`

## Dependencies

- **Backend:** нет (текущий API достаточен)
- **Studio:** Stage 0, Stage 1

## Links

- Epic: [../README.md](../README.md)
- Stage 1: [stage_01_agent_scanner.md](stage_01_agent_scanner.md)
- Stage 3: [stage_03_skill_push.md](stage_03_skill_push.md)