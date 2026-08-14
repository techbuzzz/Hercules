# Task 75 — DI lifetime fixes: captive dependency and AgentCore singleton

**Phase:** 5
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `di-lifetime-fixes`

## Goal
Два DI-бага ставят под угрозу correctness и HA:

1. **Captive dependency (H6):** `LayeredMemoryManager` зарегистрирован как singleton (`Program.cs:232`, `WebApi/Program.cs:261`), но инжектит `IWorkingMemory` который зарегистрирован как scoped. В WebAPI scoped-сервис резолвится один раз для синглтона и разделяется между всеми HTTP-запросами — per-request изоляция теряется, происходит cross-request memory contamination.

2. **AgentCore singleton с mutable state (H7):** `AgentCore` зарегистрирован как singleton (`Program.cs:526`, `WebApi/Program.cs:425`) но хранит per-session mutable state: `_transcript` (List), `_currentToolTrace`, `_lastInput`, `SessionId` (`AgentCore.cs:105-111,161`). Все HTTP-запросы делят один `AgentCore` и один transcript. `WebApiAdapter.EnsureSessionStarted()` (`:499`) стартует одну сессию на весь процесс.

## Acceptance criteria
### Sub-tasks
- [ ] **H6 fix — LayeredMemoryManager:** сделать `LayeredMemoryManager` scoped (per-request в WebAPI, per-session в CLI). Проверить что все его зависимости (`IWorkingMemory`, fact/episode stores) тоже scoped или singleton-safe.
- [ ] Альтернатива (если `LayeredMemoryManager` должен быть singleton по perf-причинам): сделать `IWorkingMemory` singleton и добавить per-request context через `AsyncLocal<WorkingMemoryContext>` или externalize state в `ISessionStore` keyed by `SessionId`. Документировать выбор.
- [ ] **H7 fix — AgentCore:** preferred approach — externalize session state. Создать `ISessionStateStore` (in-memory `ConcurrentDictionary<string, SessionState>` + optional SQLite): `GetOrCreate(sessionId)` возвращает `SessionState { Transcript, CurrentToolTrace, LastInput, SessionId }`. `AgentCore` остаётся singleton, но читает/пишет state через store keyed by `SessionId`.
- [ ] `Agent/AgentCore.cs` — убрать поля `_transcript`, `_currentToolTrace`, `_lastInput`, `SessionId`; заменить на `_sessionState.GetOrCreate(sessionId)` в начале `HandleAsyncCore`.
- [ ] `Agent/AgentCore.cs:169` — `Transcript` getter: `=> _sessionState.GetOrCreate(SessionId).Transcript.ToList()` (или убрать getter если не используется externally).
- [ ] `Agent/AgentCore.cs:486,498,1049` — все обращения к `_transcript.Add` заменить на `_sessionState.GetOrCreate(sessionId).Transcript.Add` под локом `SessionState.TranscriptLock`.
- [ ] `Agent/WebApiAdapter.cs:499` — `EnsureSessionStarted()` должен создавать/восстанавливать session per-request (или per `X-Session-Id` header), не один на процесс.
- [ ] Создать `Agent/ISessionStateStore.cs` + `Agent/SessionStateStore.cs` (in-memory) + optional `Agent/SqliteSessionStateStore.cs`.
- [ ] `Program.cs` + `WebApi/Program.cs` — зарегистрировать `ISessionStateStore` (singleton — это store, не state).
- [ ] Unit-тест: два параллельных `AgentCore.HandleAsync` с разными `sessionId` не делят transcript.
- [ ] Unit-тест: DI validation — resolve `LayeredMemoryManager` из двух разных scopes даёт разные инстансы (если scoped) ИЛИ `IWorkingMemory` resolved per-scope (если split).
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Agent/AgentCore.cs, src/agent/Agent/WebApiAdapter.cs, src/agent/Agent/ISessionStateStore.cs (new), src/agent/Agent/SessionStateStore.cs (new), src/agent/Memory/Layers/LayeredMemoryManager.cs, src/agent/Memory/IWorkingMemory.cs, src/agent/Memory/WorkingMemoryService.cs, src/agent/Program.cs, src/agent/Hercules.WebApi/Program.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_011 — layered-memory](task_011.md)
- блокирует / опирается на: [task_005 — interfaces](task_005.md)

## Risks / Rollback
Externalization session state —较大 refactor `AgentCore`. CLI single-session model должен сохраниться (один sessionId на процесс). Rollback: вернуть singleton `AgentCore` с mutable state (но баг останется). Тесты WebAPI с параллельными запросами — must-have.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)