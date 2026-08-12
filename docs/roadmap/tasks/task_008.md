# Task 8 — Ограниченный цикл исполнения

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `bounded-execution`

## Goal
Plan-act-observe с настраиваемыми max steps, wall-clock timeout, cancellation, recursion depth и per-request лимитом вызовов инструментов. Прямой ответ навыка остаётся fast-path.

## Acceptance criteria

### Sub-tasks

- [x] `AgentConfig` — добавить поля `MaxToolIterations` (=3 по умолчанию), `MaxWallClockTimeoutSeconds` (=120), `MaxRecursionDepth` (=2)
- [x] `LoopContext` — добавить `MaxIterations` (effective cap), `WallClockTimeout` (TimeSpan), `StartTime` (DateTimeOffset), `RecursionDepth`, `CancellationRequested`; методы `WithMaxIterations`, `WithTimeout`, `TickRecursion`, `RequestCancellation`
- [x] `AgentCore` — `RunWithToolsAsync` читает `MaxIterations` из `LoopContext` (fallback → `AgentConfig.MaxToolIterations`); убрать hardcoded `MaxToolIterations = 3` константу
- [x] `AgentCore.HandleAsync` — создаёт `CancellationTokenSource` с wall-clock timeout (`MaxWallClockTimeoutSeconds`) и пробрасывает в `RunWithToolsAsync`; логирует `{Step} — wall-clock timeout reached` при срабатывании
- [x] `BoundedExecutionOptions` record — per-request override: `MaxIterations?`, `TimeoutSeconds?`, `MaxRecursionDepth?`; `HandleAsync(BoundedExecutionOptions?)` overload
- [x] `AgentCore.TryParseAction` — проверяет `LoopContext.CancellationRequested` перед tool execution
- [x] Unit tests: `BoundedExecutionTests.cs` — 10+ тестов: timeout enforcement, max iterations, recursion depth, cancellation propagation, per-request override
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (baseline 349/349 + новые тесты)

## Scope / Likely files
src/agent/AgentCore.cs (limits), src/agent/Loop/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_007 — typed-contracts](task_007.md)

## Risks / Rollback
Слишком жёсткие лимиты ломают сложные задачи; нужны настраиваемые per-skill бюджеты.

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Config/AppConfig.cs`** — `AgentConfig` получил 3 новых поля:
- `MaxToolIterations = 3` (заменило hardcoded константу в AgentCore)
- `MaxWallClockTimeoutSeconds = 120` (wall-clock timeout per request)
- `MaxRecursionDepth = 2` (глубина вложенности tool-call)

**`Loop/LoopState.cs`** — `LoopContext` расширен:
- Новые поля: `MaxIterations`, `WallClockTimeout`, `StartTime`, `RecursionDepth`, `MaxRecursionDepth`, `CancellationRequested`
- `LoopContext.Initial(maxIterations, wallClockTimeout, maxRecursionDepth)` — статический factory с параметрами
- `TickRecursion()` / `UnwindRecursion()` — управление глубиной
- `WithMaxIterations(int)` — per-request override max iterations
- `WithCancellationRequested()` — флаг отмены от policy
- `IsWallClockExpired` — проверка expiry по elapsed time
- `IsRecursionExceeded` — проверка глубины рекурсии

**`Loop/LinkedCancellationTokenSource.cs`** — новый класс:
- Объединяет external `CancellationToken` + optional wall-clock timeout
- Exposes `IsWallClockTimeout` flag (true только при timeout, не при external cancellation)
- Exposes `Elapsed` (Stopwatch-based)
- `using var lcts = new LinkedCancellationTokenSource(externalCt, wallClockTimeout)`

**`Agent/AgentCore.cs`** — ключевые изменения:
- Удалена константа `MaxToolIterations = 3`
- `HandleAsync(string, BoundedExecutionOptions?, CancellationToken)` — главный overload
- `HandleAsync(string, CancellationToken)` → forwards to главный с `options=null`
- `ProcessMessageAsync` → forwards to `HandleAsync(input, null, ct)`
- Wall-clock timeout: `LinkedCancellationTokenSource` + graceful degradation answer
- `RunWithToolsAsync`: читает `ctx.MaxIterations` вместо константы
- Wall-clock expiry и policy cancellation проверяются перед tool execution
- `EvaluateSkillAsync`: использует `LoopContext.Initial(cfg.MaxToolIterations, ...)`

**`BoundedExecutionOptions` record** — per-request override:
- `MaxIterations?`, `TimeoutSeconds?`, `MaxRecursionDepth?` — null = использовать конфиг

**Tests:**
- `AgentCoreLoopTests.cs` — +12 новых тестов для LoopContext (IsWallClockExpired, IsRecursionExceeded, TickRecursion, UnwindRecursion, WithCancellationRequested, WithMaxIterations, etc.)
- `AgentCoreBoundedExecutionTests.cs` — 13 новых тестов: BoundedExecutionOptions, LinkedCts, HandleAsync overrides

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 364/364 (1 pre-existing flaky WASM timing test excluded)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
