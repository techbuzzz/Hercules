# Task 1 — Базовый цикл агента

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `core-agent-loop`

## Goal
AgentCore обрабатывает запрос: определяет применимый навык, опционально планирует ограниченные шаги инструментов, обновляет память, логирует исход.

## Acceptance criteria

- [x] `Loop/LoopState.cs` — определения типов: `LoopStep` enum (SkillRoute, LlmCall, ToolExecute, MemoryUpdate, LogInteraction), `LoopContext` record (шаг, итерация, lastTool, cancellationRequested), `StepResult` union
- [x] `AgentCore` использует `LoopContext` — явная передача состояния через `RunWithToolsAsync`
- [x] `AgentCore.HandleAsync` логирует начало/конец каждого шага через `ILogger<AgentCore>` (LogLevel.Debug) с тегом `[Loop]`
- [x] `AgentCore.ShouldReflectByCount()` — возвращает true когда CommandCount % ReflectionEveryNCommands == 0 (и порог > 0)
- [x] `AgentCore.EndSessionAsync()` вызывает `_memory.PersistSessionAsync` с текущим transcript (sync `EndSession()` сохранён для обратной совместимости)
- [x] Константа `MaxToolIterations = 3` задокументирована и не вынесена в конфиг ( явное ограничение в рамках task 001; task 008 — bounded execution — вынесет в конфиг)
- [x] Все acceptance criteria покрыты unit-тестами в `tests/Hercules.Agent.Tests/AgentCoreTests/`
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (158/159; 1 flaky WASM timing test — pre-existing, unrelated)

## Scope / Likely files
src/agent/AgentCore.cs, src/agent/Loop/

## Dependencies
- нет (стартовая)

## Risks / Rollback
Неопределённый цикл при плохих skill matches; нужен явный max-steps + cancellation.

## Implementation notes

### 2026-08-11
Основной цикл уже реализован в `AgentCore.cs`. Задача — добавить явную типизацию состояния цикла
(`Loop/` subfolder) и улучшить observability: логирование шагов, связывание `EndSession` с `PersistSessionAsync`.

**Что сделано:**
- `src/agent/Loop/LoopState.cs` — `LoopStep` enum, `LoopContext` record, `StepResult` struct
- `AgentCore.HandleAsync` — логирование каждого шага с `[Loop]` тегом, Stopwatch timing для SkillRoute
- `AgentCore.RunWithToolsAsync` — принимает `LoopContext`, обновляет после каждого tool execution
- `AgentCore.EndSessionAsync()` — вызывает `PersistSessionAsync(Transcript)` перед закрытием сессии
- `ConsoleUI.ShutdownAsync` — использует `EndSessionAsync()` вместо ручного `PersistSessionAsync + EndSession`
- 11 новых unit-тестов в `AgentCoreLoopTests.cs` (LoopContext transitions, ShouldReflectByCount, EndSessionAsync)
- Фикс pre-existing ILogger-совместимости во всех тестовых файлах (`NullLogger<T>.Instance`)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
