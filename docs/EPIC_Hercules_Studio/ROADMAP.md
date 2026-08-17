# Hercules Studio — Roadmap

> Связанные документы: [README.md](README.md), [SYSTEM-DESIGN.md](SYSTEM-DESIGN.md), [tasks/](tasks/)

## Обзор

9 этапов разработки Studio + backend prerequisites. Каждый этап = отдельный task-файл в [tasks/](tasks/).

```
Stage 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9
 └ skeleton  └ chat  └ mesh └ cfg └ cons─┬─ workflow └ pack
                                          └─ hercules-workflow-server (backend)
```

**Параллельно:** backend prerequisites (Phase 8 в [backlog.md](../roadmap/backlog.md)) выполняются до или вместе с соответствующими этапами Studio.

---

## Stage 0 — Skeleton + API Codegen (1-2 недели)

**Цель:** Запускаемый skeleton Electron + Vue + Vite + IPC + layout. API codegen pipeline (openapi-typescript + Orval + Vue Query) настроен и генерирует TS client из OpenAPI документа агента.

**Зависимости от бэкенда:**
- `task_109` — AddOpenApi() в Program.cs (OpenAPI 3.1 producer)
- `task_110` — WithTags на все контроллеры (domain grouping для Orval tags-split)
- `task_111` — Produces\<T\>() + DTO рефакторинг (исключить анонимные типы)
- `task_112` — WithName на Marketplace + Template (operationId для Orval)

**Studio tasks:**
- `task_113` — openapi-typescript + openapi-fetch + Orval setup
- `task_114` — migrate stores to Vue Query hooks

**Результат:** Studio запускается, можно добавить агента по URL, виден manifest. License consent при first-run. Empty state с marketing carousel. API client auto-generated из openapi.json (types + Vue Query hooks + Zod + MSW mocks).

📄 [tasks/stage_00_skeleton.md](tasks/stage_00_skeleton.md)

---

## Stage 1 — Agent Scanner + Connection Manager + CheckIn/CheckOut (1-2 недели)

**Цель:** Multi-agent management + сканирование машины + checkin/checkout.

**Зависимости от бэкенда:**
- `task_096` — Port migration 5000 → 8421 (отдельный PR до старта)
- `task_097` — Dual API keys (contribute + system)
- `task_098` — CheckIn/CheckOut protocol

**Результат:** Studio находит агентов на машине (port scan + process scan), подключается, переключается между ними. CheckIn/CheckOut с heartbeat.

📄 [tasks/stage_01_agent_scanner.md](tasks/stage_01_agent_scanner.md)

---

## Stage 2 — Chat + Skill Editor (2-3 недели)

**Цель:** Паритет с hercules-web по чату + полноценный skill editor с Monaco.

**Зависимости от бэкенда:** нет (текущий API достаточен)

**Результат:** Чат с активным агентом, история сессий, skill editor (prompt.md, meta.json, description.md), version diff, lifecycle actions, C# basic syntax highlighting, "Test in chat".

📄 [tasks/stage_02_chat_skills.md](tasks/stage_02_chat_skills.md)

---

## Stage 3 — Skill Authoring + Push (1-2 недели)

**Цель:** Создание навыков в Studio и push на агента.

**Зависимости от бэкенда:** нет (текущий API: POST/PUT/skills/import)

**Результат:** Push skill (POST/PUT/import), cross-agent install, skill templates (3-5 базовых + file-based .NET examples), pre-check через manifest validate + DangerousCodeScanner.

📄 [tasks/stage_03_skill_push.md](tasks/stage_03_skill_push.md)

---

## Stage 4 — Mesh Explorer (2 недели)

**Цель:** Визуальная карта mesh + управление peer'ами.

**Зависимости от бэкенда:** нет (текущий mesh API)

**Результат:** Vue Flow graph topology (1-hop), node details, router explorer, shared memory browser, circuit breaker panel, auto-refresh 30s.

📄 [tasks/stage_04_mesh_explorer.md](tasks/stage_04_mesh_explorer.md)

---

## Stage 5 — Tool Registry + MCP Management (1-2 недели)

**Цель:** Управление инструментами и MCP-серверами на агенте.

**Зависимости от бэкенда:** нет (текущий tools/MCP API; MCP add/remove через PATCH config, warning restart до Stage 6)

**Результат:** Tools list (enable/disable/health), MCP servers (list/add/edit/delete/reload), pre-check MCP before push.

📄 [tasks/stage_05_tools_mcp.md](tasks/stage_05_tools_mcp.md)

---

## Stage 6 — Тонкая настройка + Restart + SkillSdk + Context Distillation + Postgres (2-3 недели)

**Цель:** Полная настройка агента + удалённый restart + C# sandbox integration + context compression + centralized storage.

**Зависимости от бэкенда:**
- `task_099` — `POST /api/system/restart` (supervisor protocol)
- `task_100` — `McpClientService : IConfigReload` (MCP hot-reload)
- `task_101` — SkillSdk (`Hercules.SkillSdk` NuGet + `HerculesSkillContext`)
- `task_102` — Context distillation (`/api/context/distill`, `/api/context/summary`)
- `task_103` — Postgres session store (`ISessionStore` abstraction)

**Результат:** LLM/roles/mesh/quotas/context budget editor, raw config editor с diff, restart button (supervisor protocol), MCP hot-reload (без restart), C# file-based apps test-run через sandbox, context distillation UI, Postgres centralized storage config.

📄 [tasks/stage_06_config_restart.md](tasks/stage_06_config_restart.md)

---

## Stage 7 — Консилиум (2-3 недели)

**Цель:** Параллельный chat с N агентами + агрегация.

**Зависимости от бэкенда:** нет (Studio orchestrates parallel `/api/chat`)

**Результат:** Multi-agent chat (columns side-by-side), LLM-judge (один агент-судья) + Manual pick. Voting + Merge = next gen. Notifications для async operations.

📄 [tasks/stage_07_consensus.md](tasks/stage_07_consensus.md)

---

## Stage 8 — BPMN Workflow Designer (3-4 недели)

**Цель:** Визуальное проектирование workflow между агентами.

**Зависимости от бэкенда:**
- `task_104` — hercules-workflow-server (отдельный .NET сервис, workflow engine, `/api/workflows/*`, clientId/clientSecret auth, webhook + cron triggers)
- `task_105` — Workflow graph model + executor
- `task_106` — DelegatedTask persistence (SQLite)
- `task_107` — Parent/child task relationships
- `task_108` — DurableTask checkpoint persistence

**MVP (Stage 8a):** Studio-orchestrator. Linear sequence + Conditional. Graph stored в SQLite.workflows. Studio executor дёргает `/api/mesh/intent`. Long-poll monitoring.

**Production (Stage 8b):** hercules-workflow-server. Полный BPMN (parallel/exclusive/inclusive gateways, timer/error/escalation events, sub-processes). Graph stored в workflow-server. Workflow templates (3-5 + 1-2 corporate). Human-in-the-loop (AwaitingInputContext).

📄 [tasks/stage_08_workflow.md](tasks/stage_08_workflow.md)

---

## Stage 9 — Packaging & Polish (1-2 недели)

**Цель:** Production-ready Windows installer + tray + auto-updater + tests.

**Результат:** NSIS installer + portable zip, tray icon, auto-updater (GitHub Releases), native notifications, CI (GitHub Actions Windows), Vitest unit tests, Playwright E2E, Biome lint, TS strict.

📄 [tasks/stage_09_packaging.md](tasks/stage_09_packaging.md)

---

## Сводная таблица

| Stage | Срок | Backend deps | Ключевой результат |
|---|---|---|---|
| 0 | 1-2 нед | task_109-112 (OpenAPI) + task_113-114 (Studio codegen) | Skeleton, license, empty state, API codegen pipeline |
| 1 | 1-2 нед | task_096-098 | Scanner, connections, checkin/checkout |
| 2 | 2-3 нед | — | Chat, skill editor (Monaco) |
| 3 | 1-2 нед | — | Skill push, cross-agent, templates |
| 4 | 2 нед | — | Mesh explorer (Vue Flow) |
| 5 | 1-2 нед | — | Tools + MCP management |
| 6 | 2-3 нед | task_099-103 | Config, restart, SkillSdk, distillation, Postgres |
| 7 | 2-3 нед | — | Consensus (multi-agent chat) |
| 8 | 3-4 нед | task_104-108 | BPMN workflow (MVP → production) |
| 9 | 1-2 нед | — | Packaging, tray, auto-update, tests |
| **Total** | **~16-26 нед** | **21 backend tasks** | **Hercules Studio 0.6.x** |

> **Параллельно с Studio:** task_115-116 (Web-UI codegen + migration) — тот же OpenAPI pipeline для hercules-web.

## Совместимость Studio ↔ Agent

| Studio | Agent version | Notes |
|---|---|---|
| 0.1.x (Stage 0-2) | + OpenAPI producer (task_109-112) + port migration (task_096) | Basic: chat, skills, config. API codegen pipeline active |
| 0.2.x (Stage 1-3) | + dual API keys (task_097) + CheckIn/CheckOut (task_098) | Scanner, multi-agent, push |
| 0.3.x (Stage 4-5) | текущая | Mesh, tools, MCP |
| 0.4.x (Stage 6) | + restart (task_099) + MCP reload (task_100) + SkillSdk (task_101) + distillation (task_102) + Postgres (task_103) | Full config, C# skills, restart |
| 0.5.x (Stage 7) | текущая | Consensus |
| 0.6.x (Stage 8-9) | + workflow-server (task_104-108) | BPMN workflows, packaging |

> **Web-UI** parallel migration: task_115-116 (openapi-typescript + Orval + Vue Query) — hercules-web переходит на generated API client одновременно со Studio.