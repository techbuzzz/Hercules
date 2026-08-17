# Task 5 — Интерфейсы

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `interfaces`

## Goal
CLI REPL, Telegram-бот, ASP.NET Core Minimal API и Astro web UI поверх одного application service и policy-слоя.

## Acceptance criteria

- [x] `src/hercules-web/src/lib/api.ts` — добавить методы lifecycle API: `evaluateSkill`, `deprecateSkill`, `rollbackSkill`, `undeprecateSkill`, `improveSkill`, `getDeprecatedSkills`, `getSkillEvaluationResult`
- [x] `src/hercules-web/src/pages/skills.astro` — полная страница навыков с действиями: evaluate, deprecate/rollback/undeprecate, improve (AI), кнопка создания навыка
- [x] `src/hercules-web/src/pages/stats.astro` — отображение budget (месячный лимит, daily breakdown) и audit log
- [x] `src/hercules-web/src/pages/skills.astro` — модальные диалоги: create skill (name/trigger/prompt), improve skill (AI-assisted)
- [x] `src/agent/Telegram/TelegramBot.cs` — расширить обработку: /evaluate {skillId}, /deprecate {skillId} {reason}, /rollback {skillId}, команды lifecycle-ответов
- [x] `src/agent/CLI/ConsoleUI.cs` — убедиться что lifecycle команды (`/skills evaluate`, `/skills deprecate`, `/skills rollback`) покрыты (уже есть через SkillManager)
- [x] `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- [x] `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- [x] `astro build` hercules-web — 0 errors
- [x] `dotnet test` — baseline passed (217+)

## Scope / Likely files
src/agent/CLI/, src/agent/Telegram/, src/agent/Hercules.WebApi/, src/hercules-web/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)

## Risks / Rollback
Дрейф UX между интерфейсами; единый presentation-слой обязателен.

## Implementation notes

### 2026-08-12

**Добавлено:**

**`src/hercules-web/src/lib/api.ts`** — новые TypeScript-типы и методы:
- `SkillEvaluationResultDto`, `DeprecatedSkillsDto`, `AuditEntryDto`, `AuditLogDto`, `BudgetDto`, `BudgetMonthlyDto`
- `evaluateSkill(id)` → POST /api/skills/{id}/evaluate
- `deprecateSkill(id, reason)` → POST /api/skills/{id}/deprecate
- `rollbackSkill(id)` → POST /api/skills/{id}/rollback
- `undeprecateSkill(id)` → POST /api/skills/{id}/undeprecate
- `improveSkill(id)` → POST /api/skills/{id}/improve
- `getDeprecatedSkills()` → GET /api/skills/deprecated
- `getBudget(days)` → GET /api/budget
- `getBudgetMonthly(limitUsd)` → GET /api/budget/monthly
- `getAudit(limit)` → GET /api/audit
- `getAuditByTarget(target, limit)` → GET /api/audit/{target}

**`src/hercules-web/src/pages/skills.astro`** — расширенная страница навыков:
- Кнопка "Deprecated" для показа/скрытия deprecated-навыков
- Кнопка "Оценить" → вызывает evaluateSkill, показывает PASS/FAIL + toast
- Кнопка "Deprecated" → prompt для ввода причины → deprecateSkill
- Deprecated-карточки с кнопками "Откат" и "Восстановить"
- Toast-уведомления для всех действий (ok/err)
- Оценка последнего запуска отображается на карточке навыка

**`src/hercules-web/src/components/StatsDashboard.astro`** — расширенный дашборд:
- Секция "Бюджет LLM (30 дней)" — вызовы, токены, стоимость, daily chart, месячная сводка с предупреждением о превышении лимита
- Секция "Аудит" — последние 50 событий с actor/action/target
- Refresh-кнопки для budget и audit

**`src/agent/Telegram/TelegramBot.cs`** — расширенный Telegram-бот:
- `/evaluate [id]` — запуск оценки навыка через SkillLifecycleService
- `/deprecate [id] [reason]` — депрекация навыка
- `/rollback [id]` — откат к предыдущей версии
- `/help` — справка по всем командам
- SkillLifecycleService добавлен в конструктор (PRIMARY CONSTRUCTOR)
- SkillManager регистрирует DeprecatedAt для визуального отличия deprecated-навыков в /skills

**`src/agent/Program.cs`** — добавлена DI-регистрация:
- `SkillLifecyclePolicy`, `SkillDeprecationManager`, `SkillEvaluationEngine`, `SkillLifecycleService`

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 254/254 passed
- `astro build` hercules-web — 0 errors, 5 pages built

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
