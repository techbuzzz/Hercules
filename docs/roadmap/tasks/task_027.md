# Task 27 — Сборка и сжатие контекста

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `context-assembly`

## Goal
ContextBuilder выбирает релевантную память, схемы инструментов, примеры и prior task state в рамках token-бюджета; завершённые tool traces сжимаются в episodic memory.

## Acceptance criteria

### Sub-tasks

- [x] `Context/ContextBuilderConfig.cs` — config: `MaxContextTokens` (default 6000), `MaxFactsInContext` (default 20), `MaxEpisodesInContext` (default 5), `MaxToolSchemasInContext` (default 10), `Enabled` (default true), `CompressionThreshold` (default 3 tool calls)
- [x] `Context/Models.cs` — `ContextItem` record (Type, Content, TokenEstimate, Importance, Source, Tags), `ContextBudget` record (MaxTokens, UsedTokens, RemainingTokens), `ToolTraceEntry` record (ToolName, Input, Output, DurationMs, Timestamp), `ContextAssembly` record
- [x] `Context/IContextBuilder.cs` — interface: `BuildContextAsync`, `CompressTraceAsync`, `EstimateTokens`, `GetCurrentBudget`
- [x] `Context/ContextBuilder.cs` — реализация: использует LayeredMemoryManager + EpisodicStore; `BuildContextAsync` приоритизирует high-confidence facts → medium → working memory → episodes; укладывается в token budget; фильтрует sensitive данные; возвращает `ContextAssembly`; `CompressTraceAsync` записывает compressed trace в episodic store
- [x] `Context/Summarizer/ITraceSummarizer.cs` + `TraceSummarizer.cs` — `Summarize()`: группирует tool calls по имени, агрегирует avg duration, success/fail count; Truncate helper для output snippets
- [x] `ContextController.cs` — WebAPI: `GET /api/context/budget`, `GET /api/context/summary`, `POST /api/context/trace/compress`
- [x] `AgentCore.cs` — интеграция: `BuildSystemPrompt` принимает `contextBlock` параметр; `HandleAsyncCore` вызывает `IContextBuilder.BuildContextAsync`; `_currentToolTrace` записывает каждую tool call entry; `CompressTraceAsync` вызывается после каждого запроса (fire-and-forget); `LayeredMemoryManager` опционален (nullable, backward-совместим)
- [x] `AppConfig.cs` — добавлена `ContextConfig` в `AppConfig` root
- [x] `LayeredMemoryManager.cs` — добавлен `EpisodicStore` accessor для `ContextBuilder`
- [x] `Program.cs` (CLI + WebAPI) — зарегистрирован `ContextBuilderConfig`, `ITraceSummarizer`, `IContextBuilder`; `ContextController` подключён
- [x] Unit-тесты: `ContextBuilderTests.cs` — 13 тестов: disabled mode, empty memory, facts, max facts limit, token estimation, budget, trace compression threshold
- [x] Unit-тесты: `TraceSummarizerTests.cs` — 6 тестов: empty trace, single call, multiple calls same tool, mixed tools, failed calls, long output truncation
- [x] `dotnet build` проходит без warnings (NU1902 на OTel.Api — pre-existing)
- [x] `dotnet test` проходит (802 total: 794 pass, 8 pre-existing failures: OtelService × 5, BudgetGuard × 1, WasmTool × 1, AgentCoreBoundedExecution × 1 — unrelated to task_027)</parameter>


## Scope / Likely files
src/agent/Context/ContextBuilder.cs, src/agent/Context/Summarizer/

## Dependencies
- блокирует / опирается на: [task_011 — layered-memory](task_011.md)
- блокирует / опирается на: [task_012 — budget-guardrails](task_012.md)

## Risks / Rollback
Потеря важного контекста при сжатии; importance-score.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
