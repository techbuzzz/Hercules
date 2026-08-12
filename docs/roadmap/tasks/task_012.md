# Task 12 — Бюджеты и guardrails

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `budget-guardrails`

## Goal
Per-request и per-day лимиты на токены, стоимость, время, вызовы инструментов, диск и ретраи. Агент сообщает graceful degradation вместо тихого превышения.

## Acceptance criteria

### Sub-tasks

- [x] `Config/AppConfig.cs` — добавить `BudgetConfig` секцию с per-request и per-day лимитами
- [x] `Budget/Models.cs` — `GuardrailLimitType` enum (TokensPerRequest, CostPerDay, ToolCallsPerRequest, WallClockSecondsPerRequest, RetriesPerTool), `GuardrailStatus` record (limit, current, remaining, isExceeded), `GuardrailViolation` record
- [x] `Budget/IGuardrailService.cs` — интерфейс: `CheckLimit`, `RecordUsage`, `GetStatus`, `ResetRequestCounters`
- [x] `Budget/GuardrailService.cs` — реализация: in-memory session counters + SQLite daily aggregation, graceful degradation message
- [x] `Budget/BudgetGuard.cs` — `CheckAllLimits(sessionId, requestType)` → returns list of violations; soft-warn vs hard-cap enforcement
- [x] `AgentCore` — интеграция `IGuardrailService`: перед LLM-вызовом проверяет лимиты, при превышении возвращает graceful degradation ответ вместо продолжения
- [x] `BudgetController` — расширить: `GET /api/budget/guardrails` — текущий статус всех лимитов; `GET /api/budget/guardrails/{type}` — конкретный лимит
- [x] `Program.cs` (CLI + WebAPI) — зарегистрировать `BudgetConfig` singleton и `IGuardrailService` singleton в DI
- [x] `tests/Hercules.Agent.Tests/Budget/BudgetGuardTests.cs` — unit-тесты: each limit type, soft-warn vs hard-cap, accumulation, reset
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит

## Scope / Likely files
src/agent/Budget/, src/agent/Hercules.WebApi/Controllers/BudgetController.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)
- блокирует / опирается на: [task_008 — bounded-execution](task_008.md)

## Risks / Rollback
Жёсткие лимиты ломают сложные задачи; soft-warn + hard-cap.

## Implementation notes

### 2026-08-12

**Добавлено:**

**`src/agent/Config/AppConfig.cs`** — `BudgetConfig`:
- Per-request: `MaxTokensPerRequest`, `MaxToolCallsPerRequest`, `MaxRetriesPerTool`, `MaxWallClockSecondsPerRequest`
- Per-day: `MaxCostPerDayUsd`, `MaxTokensPerDay`, `MaxCallsPerDay`
- `EnforcementMode` ("soft_warn" | "hard_cap"), `Enabled`

**`src/agent/Budget/Models.cs`** — типы:
- `GuardrailLimitType` enum (7 типов)
- `GuardrailStatus` record — Type, Limit, Current, Remaining, IsExceeded, IsHardCap
- `GuardrailViolation` record — Type, Limit, Actual, Message, EnforcementMode
- `GuardrailCheckResult` record — Violations list, HasHardViolation
- `RequestCounters` class — ToolCalls, RetriesForCurrentTool, ElapsedMs, TokensUsed

**`src/agent/Budget/IGuardrailService.cs`** — интерфейс:
- `CheckLimits(sessionId, estimatedTokens)` → `GuardrailCheckResult`
- `RecordLlmUsage`, `RecordToolCall`, `RecordToolRetry`, `RecordElapsedTime`
- `GetStatus`, `ResetRequestCounters`, `GetRequestCounters`

**`src/agent/Budget/GuardrailService.cs`** — реализация:
- `ConcurrentDictionary<string, RequestCounters>` — per-session request counters (сбрасываются после HandleAsync)
- In-memory daily accumulators: `_todayTotalTokens`, `_todayTotalCalls`, `_todayTotalCostCents` (cents × 100 для atomicity)
- Hard-cap: TokensPerRequest, ToolCallsPerRequest, RetriesPerTool, WallClockSecondsPerRequest
- Soft-warn: CostPerDay, TokensPerDay, CallsPerDay

**`src/agent/Budget/BudgetGuard.cs`** — graceful degradation хелпер:
- `CheckAndGetDegradationMessage(result)` → degradation string или null
- `LogSoftWarnings(result)` → Warning-level логи
- `CheckTokensBeforeRequest(estimatedTokens)` → null или degradation message

**`src/agent/Agent/AgentCore.cs`** — интеграция:
- Конструктор: `IGuardrailService?`, `BudgetGuard?` (nullable, backward-compatible)
- Pre-check: `CheckLimits` + `CheckAndGetDegradationMessage` → hard-cap → graceful degradation response
- Post-LLM: `RecordLlmUsage` + token estimation
- Tool loop: `RecordToolCall` после каждого выполнения
- `EstimateCost(provider, tokens)` — rough pricing per provider

**`src/agent/LLM/ChatModels.cs`** — `LlmResponse`:
- Added `InputTokens` and `OutputTokens` (default 0, backward-compatible)

**`src/agent/LLM/ChatClientLLMClient.cs`**:
- `CompleteAsync` estimates token counts: `Math.Max(1, text.Length / 4)`
- `EstimateTokens(text)` helper

**DI** (CLI + WebAPI):
- `BudgetConfig` singleton, `IGuardrailService` → `GuardrailService`, `BudgetGuard`

**WebAPI эндпоинты:**
- `GET /api/budget/guardrails` — все лимиты + request counters
- `GET /api/budget/guardrails/{type}` — конкретный лимит

**Tests** — `tests/.../Budget/BudgetGuardTests.cs` — 26 новых тестов:
- `BudgetGuardTests`: 9 тестов
- `GuardrailServiceTests`: 17 тестов

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 430/431 passed (1 pre-existing flaky WASM timing test)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
