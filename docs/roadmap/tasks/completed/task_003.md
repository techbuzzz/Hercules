# Task 3 — Гибридное хранилище

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `hybrid-storage`

## Goal
Markdown/JSON для навыков и человеко-читаемой памяти; SQLite для сессий, оценок, метрик, бюджетов, аудита и устойчивого состояния задач. Без внешних зависимостей.

## Acceptance criteria

### Sub-tasks

- [x] `Storage/Models.cs` — добавить `BudgetEntry`, `AuditLogEntry`, `SkillEvaluationResult`, `TaskState` record'ы
- [x] `SqliteSessionStore.InitSchema()` — добавить 4 новых таблицы: `budget_entries`, `audit_log`, `skill_evaluations`, `task_states` с индексами
- [x] `SqliteSessionStore` — добавить методы `LogBudgetEntryAsync`, `GetBudgetSummaryAsync`, `GetDailyBudgetAsync`
- [x] `SqliteSessionStore` — добавить методы `LogAuditAsync`, `GetAuditLogAsync`, `GetAuditLogByTargetAsync`
- [x] `SqliteSessionStore` — добавить методы `SaveEvaluationResultAsync`, `GetSkillEvaluationHistoryAsync`
- [x] `SqliteSessionStore` — добавить методы `SaveTaskStateAsync`, `LoadTaskStateAsync`, `ListTaskStatesAsync`
- [x] `IBudgetService` / `BudgetService` — сервис учёта budget с провайдером, моделью, датой; запрос running total
- [x] `IAuditLog` / `AuditLogService` — append-only аудит-лог с actor/target/action/details
- [x] `Program.cs` — зарегистрировать BudgetService и AuditLogService в DI
- [x] `tests/Hercules.Agent.Tests/Storage/` — unit-тесты для BudgetService и AuditLogService
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (baseline: 193/193 + новые тесты)

## Implementation notes

### 2026-08-12

**Добавлено:**

**Models.cs** — 4 новых record'а:
- `BudgetEntry` / `BudgetSummary` — учёт токенов и стоимости за вызов
- `AuditLogEntry` — actor/action/target/details/sessionId для полного аудита
- `SkillEvaluationRecord` — история оценок навыков (для трендов)
- `TaskState` — устойчивое состояние задачи (для task_018 durable task lifecycle)

**SqliteSessionStore.InitSchema()** — 4 новых таблицы + 5 индексов:
- `budget_entries` (session_id, provider, model, input/output_tokens, cost_usd, created_at)
- `audit_log` (actor, action, target, details, session_id, created_at)
- `skill_evaluations` (skill_id, score, passed, test_results, evaluator_provider, created_at)
- `task_states` (task_id UNIQUE, status, result, error, metadata, created_at, updated_at)
- Индексы: ix_budget_session, ix_budget_created, ix_audit_created, ix_audit_target, ix_eval_skill, ix_task_taskid

**BudgetService + IBudgetService** (`Storage/BudgetService.cs`):
- `LogAsync` — запись budget entry
- `GetSummaryAsync` — сводка за период
- `GetDailyAsync` — дневная разбивка
- `IsOverMonthlyBudgetAsync` — проверка месячного лимита

**AuditLogService + IAuditLog** (`Storage/AuditLogService.cs`):
- `LogAsync` — append-only аудит-событие
- `GetRecentAsync` — последние N записей
- `GetByTargetAsync` — записи по target (skill_id, session_id, etc.)

**BudgetController** (`WebApi/Controllers/BudgetController.cs`):
- `GET /api/budget` — сводка + дневная разбивка
- `GET /api/budget/monthly` — месячная сводка с проверкой лимита

**AuditController** (`WebApi/Controllers/AuditController.cs`):
- `GET /api/audit` — последние записи
- `GET /api/audit/{target}` — записи по target

**DI** — BudgetService + AuditLogService зарегистрированы в обоих Program.cs (CLI и WebAPI)

**Tests** — 24 новых теста (BudgetServiceTests × 6, AuditLogServiceTests × 7, SqliteSessionStoreHybridTests × 11)

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 217/217 passed (193 baseline + 24 new)

## Scope / Likely files
src/agent/Storage/, src/agent/Memory/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)

## Risks / Rollback
Без внешних зависимостей => сложная миграция схем; нужен плановая версионность.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
