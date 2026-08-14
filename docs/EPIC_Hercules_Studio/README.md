# EPIC — Hercules Studio

> **Статус:** Planning
> **Owner:** Victor V (techbuzzz)
> **Started:** 2026-08-14
> **License:** AGPL-3.0 (Community / Non-profit) + Commercial License для корпоративного использования

Hercules Studio — десктопное IDE-приложение (Electron + Vue 3) для управления, настройки и оркестрации агентов Hercules. Не замена `src/hercules-web` (он остаётся лёгкой веб-админкой), а **супер-расширение** для power users: подключение к нескольким агентам, тонкая настройка, редактор навыков с C# file-based apps, mesh explorer, консилиум агентов с BPMN-подобной оркестрацией.

## Документы

| Документ | Назначение |
|---|---|
| [SYSTEM-DESIGN.md](SYSTEM-DESIGN.md) | Архитектура, компоненты, потоки данных, стек |
| [ROADMAP.md](ROADMAP.md) | 9 этапов разработки с целями и зависимостями |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Детальные технические диаграммы, IPC-контракты, модели данных |
| [adr/](adr/) | Architecture Decision Records (выбор Electron, Vue, портов, auth) |
| [tasks/](tasks/) | Task-level планы для каждого этапа (stage_00 … stage_09) |

## Ключевые решения (summary)

- **Платформа:** Windows x64 (MVP), macOS/Linux позже
- **Стек:** Electron 30+ / electron-vite / Vue 3 (Composition API) / Pinia / Vite / Tailwind CSS 4 / shadcn-vue / Monaco Editor / xterm.js / Vue Flow / better-sqlite3 / Tabler Icons
- **Порт агента:** 8421-8521 (100 портов, мнемоника "степени двойки"). Дефолт 8421. Legacy 5000 сканируется как fallback
- **Auth:** Dual API keys (contribute + system) + CheckIn/CheckOut (heartbeat TTL 60s). System key = read-only monitor может подключаться параллельно. Workflow-server использует clientId/clientSecret
- **Репозиторий:** Монорепо `src/hercules-studio/` внутри Hercules. Отдельный репо позже если станет отдельным продуктом
- **Лицензия:** AGPL-3.0 + commercial license. Trust-based для MVP, license consent dialog при first-run
- **Сборка:** electron-vite для dev, electron-builder (NSIS) для packaging. GitHub Actions CI. Agent + Studio независимые версии, совместимость по API contract
- **Локальное хранилище Studio:** `userData/studio.db` (SQLite) для chat history, skill drafts, workflows, scan cache. Connections/settings в JSON + safeStorage
- **hercules-workflow-server:** Отдельный .NET сервис для workflow execution (Этап 8 production). Триггеры: webhook + cron (MVP), file watcher + event bus позже
- **SkillSdk:** NuGet-пакет `Hercules.SkillSdk` — IHttpClient, IMcpClient, ILlmClient, IMemoryClient, ISkillLogger, ISessionContext. C# file-based apps hosted в процессе агента, whitelist approach
- **Context distillation:** Hierarchical (recent=raw, older=summary, ancient=key facts). Доработка бэкенда `/api/context/*`
- **i18n:** en/ru с самого начала (vue-i18n)
- **Theme:** Dark + Light, switchable. Dark = базовый

## Скоуп Epic

### Studio (этапы 0-9)

1. **Stage 0** — Skeleton: Electron + Vue + Vite, IPC, layout, Pinia, SQLite, SDK, license consent
2. **Stage 1** — Agent Scanner + Connection Manager + CheckIn/CheckOut
3. **Stage 2** — Chat (1 агент) + Skill Editor (Monaco)
4. **Stage 3** — Skill Authoring + Push на агента
5. **Stage 4** — Mesh Explorer + визуализация topology
6. **Stage 5** — Tool Registry + MCP management
7. **Stage 6** — Тонкая настройка + restart + context distillation + Postgres centralized storage + SkillSdk
8. **Stage 7** — Консилиум (multi-agent chat + агрегация)
9. **Stage 8** — BPMN Workflow Designer (MVP Studio-orchestrator → production backend-executor)
10. **Stage 9** — Packaging: tray, auto-updater, NSIS installer, tests

### Backend prerequisites (Phase 8 в backlog)

Доработки Hercules.WebApi и новых сервисов, необходимые для Studio:

1. **Port migration** 5000 → 8421 (отдельный PR до старта Studio)
2. **Dual API keys** (contribute + system roles)
3. **CheckIn/CheckOut** protocol
4. **`POST /api/system/restart`** supervisor protocol
5. **`McpClientService : IConfigReload`** (MCP hot-reload)
6. **SkillSdk** (`Hercules.SkillSdk` NuGet + `HerculesSkillContext`)
7. **Context distillation** (`/api/context/distill`, `/api/context/summary`)
8. **Postgres session store** (`ISessionStore` abstraction)
9. **hercules-workflow-server** (отдельный .NET сервис)
10. **Workflow engine** (graph model, executor, `/api/workflows/*`)

Полный список задач prerequisite — в [docs/roadmap/backlog.md](../roadmap/backlog.md) (раздел Phase 8).

## Совместимость

| Studio | Agent | Notes |
|---|---|---|
| 0.1.x | текущая + port migration | Basic: chat, skills, config |
| 0.2.x | + dual API keys + CheckIn/CheckOut | Scanner, multi-agent |
| 0.3.x | + McpClientService reload | MCP management |
| 0.4.x | + restart protocol + SkillSdk + context distillation | Full config, C# skills |
| 0.5.x | + Postgres session store | Centralized storage |
| 0.6.x | + hercules-workflow-server | BPMN workflows |

## Ссылки

- [SYSTEM-DESIGN.md](SYSTEM-DESIGN.md) — полная архитектура
- [ROADMAP.md](ROADMAP.md) — этапы
- [tasks/](tasks/) — детальные планы задач
- [adr/](adr/) — архитектурные решения
- [../roadmap/backlog.md](../roadmap/backlog.md) — backlog Hercules (включая Phase 8 prerequisites)
- [../../PLAN-v2.md](../../PLAN-v2.md) — Stage 2 code execution (DotnetFileBasedExecutor, sandbox, SkillSdk)