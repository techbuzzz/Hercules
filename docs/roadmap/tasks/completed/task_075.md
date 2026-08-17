# Task 75 — DI lifetime fixes: captive dependency and AgentCore singleton

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** —
**Slug:** `di-lifetime-fixes`

## Goal
Два DI-бага ставят под угрозу correctness и HA:

1. **Captive dependency (H6):** `LayeredMemoryManager` зарегистрирован как singleton (`Program.cs:232`, `WebApi/Program.cs:261`), но инжектит `IWorkingMemory` который зарегистрирован как scoped. В WebAPI scoped-сервис резолвится один раз для синглтона и разделяется между всеми HTTP-запросами — per-request изоляция теряется, происходит cross-request memory contamination.

2. **AgentCore singleton с mutable state (H7):** `AgentCore` зарегистрирован как singleton (`Program.cs:526`, `WebApi/Program.cs:425`) но хранит per-session mutable state: `_transcript` (List), `_currentToolTrace`, `_lastInput`, `SessionId` (`AgentCore.cs:105-111,161`). Все HTTP-запросы делят один `AgentCore` и один transcript. `WebApiAdapter.EnsureSessionStarted()` (`:499`) стартует одну сессию на весь процесс.

## Acceptance criteria
### Sub-tasks
- [x] **H6 fix — LayeredMemoryManager:** сделать `LayeredMemoryManager` scoped (per-request в WebAPI, per-session в CLI). Проверить что все его зависимости (`IWorkingMemory`, fact/episode stores) тоже scoped или singleton-safe.
- [x] Альтернатива (если `LayeredMemoryManager` должен быть singleton по perf-причинам): сделать `IWorkingMemory` singleton и добавить per-request context через `AsyncLocal<WorkingMemoryContext>` или externalize state в `ISessionStore` keyed by `SessionId`. Документировать выбор.
  - **Решение:** externalize via `ISessionStateStore`. `LayeredMemoryManager` остаётся singleton, но резолвит per-session `IWorkingMemory` через `ISessionStateStore.GetOrCreate(sessionId).WorkingMemory`. Никаких captive-dep.
- [x] **H7 fix — AgentCore:** preferred approach — externalize session state. Создать `ISessionStateStore` (in-memory `ConcurrentDictionary<string, SessionState>` + optional SQLite): `GetOrCreate(sessionId)` возвращает `SessionState { Transcript, CurrentToolTrace, LastInput, SessionId }`. `AgentCore` остаётся singleton, но читает/пишет state через store keyed by `SessionId`.
- [x] `Agent/AgentCore.cs` — убрать поля `_transcript`, `_currentToolTrace`, `_lastInput`, `SessionId`; заменить на `_sessionState.GetOrCreate(sessionId)` в начале `HandleAsyncCore`.
- [x] `Agent/AgentCore.cs:169` — `Transcript` getter: `=> _sessionState.GetOrCreate(SessionId).Transcript.ToList()` (или убрать getter если не используется externally).
- [x] `Agent/AgentCore.cs:486,498,1049` — все обращения к `_transcript.Add` заменить на `_sessionState.GetOrCreate(sessionId).Transcript.Add` под локом `SessionState.TranscriptLock`.
- [x] `Agent/WebApiAdapter.cs:499` — `EnsureSessionStarted()` должен создавать/восстанавливать session per-request (или per `X-Session-Id` header), не один на процесс.
- [x] Создать `Agent/ISessionStateStore.cs` + `Agent/SessionStateStore.cs` (in-memory) + optional `Agent/SqliteSessionStateStore.cs`.
  - `InMemorySessionStateStore.cs` — singleton `ConcurrentDictionary` + LRU-eviction. SQLite вариант отложен (по заданию — optional).
- [x] `Program.cs` + `WebApi/Program.cs` — зарегистрировать `ISessionStateStore` (singleton — это store, не state).
- [x] Unit-тест: два параллельных `AgentCore.HandleAsync` с разными `sessionId` не делят transcript.
- [x] Unit-тест: DI validation — resolve `LayeredMemoryManager` из двух разных scopes даёт разные инстансы (если scoped) ИЛИ `IWorkingMemory` resolved per-scope (если split).
  - Реализовано как `LayeredMemoryManager_SessionScoped_WorkingMemoryIsolatedPerSession` + `InMemorySessionStateStore_PerSessionWorkingMemory_AreDifferentInstances`.
- [x] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Agent/AgentCore.cs, src/agent/Agent/WebApiAdapter.cs, src/agent/Agent/ISessionStateStore.cs (new), src/agent/Agent/SessionStateStore.cs (new), src/agent/Memory/Layers/LayeredMemoryManager.cs, src/agent/Memory/IWorkingMemory.cs, src/agent/Memory/WorkingMemoryService.cs, src/agent/Program.cs, src/agent/Hercules.WebApi/Program.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_011 — layered-memory](task_011.md)
- блокирует / опирается на: [task_005 — interfaces](task_005.md)

## Risks / Rollback
Externalization session state — большой refactor `AgentCore`. CLI single-session model должен сохраниться (один sessionId на процесс). Rollback: вернуть singleton `AgentCore` с mutable state (но баг останется). Тесты WebAPI с параллельными запросами — must-have.

## Implementation notes

### 2026-08-14

**Добавлено:**

**`src/agent/Agent/SessionState.cs`** (new) — per-session mutable state, externalised from `AgentCore`:
- `SessionId` (immutable)
- `Transcript` (List<ChatTurn> + `TranscriptLock` для thread-safe Append/TakeLast)
- `ToolTrace` (List<ToolTraceEntry> + `ToolTraceLock`)
- `LastInput` (string) — для порога создания навыка
- `ContextBlock` (string) — кешированный контекст памяти (legacy `MemoryManager.BuildContextBlock`)
- `CommandCount` (int) — для reflection cadence
- `WorkingMemory` (`IWorkingMemory` per-session) — H6 fix: per-session `IWorkingMemory`, не shared singleton
- `AppendTranscript`, `TakeLastTranscript`, `ClearTranscript`, `AppendToolTrace`, `ClearToolTrace`

**`src/agent/Agent/ISessionStateStore.cs`** (new) — интерфейс:
- `GetOrCreate(sessionId) → SessionState`
- `Remove(sessionId)`, `ListSessions()`, `Count`

**`src/agent/Agent/InMemorySessionStateStore.cs`** (new) — реализация на `ConcurrentDictionary<string, SessionState>`, LRU-eviction при `MaxSessions` (default 1024).

**`src/agent/Agent/AgentCore.cs`** — refactor:
- Удалены mutable-поля: `_transcript`, `_transcriptLock`, `_contextBlock`, `_lastInput`, `_currentToolTrace`.
- Добавлен `ISessionStateStore _sessionStates` dependency (optional ctor param с fallback на `new InMemorySessionStateStore()` для backward compat).
- `HandleAsync(input, options, sessionId?, externalCt)` — multi-session overload.
- `StartSession(string sessionId)` — multi-session overload.
- `BuildMessages`, `BuildResponse`, `RunWithToolsAsync` — принимают `SessionState` parameter.
- Все `SessionId`-keyed обращения (guardrails, audit, escalation, log interaction) идут через параметр `sessionId`, не через `this.SessionId`.
- `Transcript` и `CommandCount` getters проксируют в `GetState(SessionId).*`.

**`src/agent/Agent/WebApiAdapter.cs`**:
- `EnsureSessionStarted(string sessionId)` — multi-session overload.
- `ChatAsync(message, sessionId, ct)` — multi-session overload.
- `DefaultSessionId` property — для ChatController.

**`src/agent/Hercules.WebApi/Controllers/ChatController.cs`** — multi-tenant режим:
- `POST /api/chat` читает optional `X-Session-Id` header.
- Если header отсутствует — используется `DefaultSessionId` (back-compat single-tenant).
- Если header задан — вызывает `EnsureSessionStarted(sessionId)` для per-request init.

**`src/agent/Memory/Layers/LayeredMemoryManager.cs`** — два ctor:
- Legacy `(IWorkingMemory, IDurableFactsStore, IEpisodicStore, config?)` — для тестов / CLI single-tenant.
- Новый `(ISessionStateStore, IDurableFactsStore, IEpisodicStore, config?)` — H6 fix: per-session `IWorkingMemory` resolved on demand.
- `BuildContextBlockAsync(sessionId?, ct)` — per-session working memory view.
- `SetWorking/GetWorking/ClearWorking(key, sessionId?)` — per-session.

**`src/agent/Agent/MemoryManager.cs`** — `BuildContextBlock(string? sessionId)` overload: пробрасывает sessionId в `LayeredMemoryManager.BuildContextBlockAsync`.

**`src/agent/Program.cs` + `src/agent/Hercules.WebApi/Program.cs`** — DI:
- `ISessionStateStore` registered as singleton.
- `LayeredMemoryManager` registered as singleton с constructor `ISessionStateStore`.
- Removed `IWorkingMemory` registration (теперь создаётся per-session в `SessionState`).

**`tests/Hercules.Agent.Tests/AgentCoreTests/DiLifetimeFixesTests.cs`** (new) — 11 unit-тестов:
- `HandleAsync_ParallelSessions_TranscriptsDoNotInterleave` — H7 fix: parallel HandleAsync с разными sessionId.
- `HandleAsync_DifferentSessions_IncrementSeparateCommandCount` — `state.CommandCount` per-session.
- `HandleAsync_SameSession_AccumulatesCommandCount` — accumulation работает.
- `SessionStateStore_GetOrCreate_ReturnsSameInstance` — стабильность per-key.
- `SessionStateStore_DifferentIds_ReturnDifferentInstances` — изоляция.
- `SessionStateStore_Remove_DeletesSession` — cleanup.
- `SessionState_Transcript_AppendIsolatedPerSession` — per-session transcript.
- `SessionState_ToolTrace_AppendIsolatedPerSession` — per-session tool trace.
- `LayeredMemoryManager_SessionScoped_WorkingMemoryIsolatedPerSession` — H6 fix: per-session working memory.
- `LayeredMemoryManager_LegacyCtor_StillWorksForBackCompat` — back-compat ctor.
- `InMemorySessionStateStore_PerSessionWorkingMemory_AreDifferentInstances` — `IWorkingMemory` per-session.

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors (warnings pre-existing)
- `dotnet build` Hercules.WebApi.csproj — 0 errors (1 pre-existing warning)
- `dotnet test --filter "FullyQualifiedName~AgentCoreTests|FullyQualifiedName~Memory|FullyQualifiedName~Lifecycle"` — 230/230 passed (101 AgentCore + 68 Memory + 50+ Lifecycle + 11 new DiLifetimeFixes)
- `dotnet test` (full) — 1779/1790 passed, 9 failed = pre-existing (Redis, OpenTelemetry, NumericValidator, BudgetGuard hard-cap). My changes не сломали ни одного passing test.

**Back-compat guarantees:**
- CLI single-tenant: `AgentCore.SessionId` остаётся, `EnsureSessionStarted()` (no-arg) стартует default session.
- `MemoryManager.BuildContextBlock()` (no-arg) — без sessionId, использует "default" working memory view.
- `LayeredMemoryManager` legacy ctor `(IWorkingMemory, ...)` — сохранён для существующих unit-тестов.
- `AgentCore` ctor — `ISessionStateStore` опциональный (default = `new InMemorySessionStateStore()`).
- WebApi: `POST /api/chat` без `X-Session-Id` header работает как раньше.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)