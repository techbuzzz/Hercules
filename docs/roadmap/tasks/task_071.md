# Task 71 — SQLite thread-safety and sync-over-async removal

**Phase:** 5
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `sqlite-thread-safety`

## Goal
`SqliteSessionStore` делит одну `SqliteConnection` между всеми async- и sync-вызовами без какой-либо синхронизации. `Microsoft.Data.Sqlite` не потокобезопасен для конкурентных команд на одном соединении — конкурентные запросы бросают `SQLiteException` или портят чтения. Дополнительно ~20 sync-обёрток (`.GetAwaiter().GetResult()`) вызываются из горячих путей (`AgentCore.HandleAsyncCore`, background services), блокируя thread-pool потоки на async I/O и усугубляя гонку на соединении. Цель — сделать SQLite-доступ потокобезопасным и полностью асинхронным.

## Acceptance criteria
### Sub-tasks
- [ ] Выбрать стратегию: (A) `SemaphoreSlim(1,1)` вокруг каждой команды на существующем соединении, или (B) connection-per-operation с WAL + shared cache (рекомендуется для throughput). Документировать выбор в `Implementation notes`.
- [ ] `Storage/SqliteSessionStore.cs` — убрать все sync-обёртки (`LogInteraction`, `GetSession`, `CreateSession`, и т.д. ~20 методов); оставить только `*Async` варианты.
- [ ] Если стратегия A: добавить `private readonly SemaphoreSlim _connLock = new(1,1);` и обернуть каждый `*Async` метод в `await _connLock.WaitAsync(ct); try { ... } finally { _connLock.Release(); }`.
- [ ] Если стратегия B: убрать поле `_conn`; каждый метод открывает `using var conn = new SqliteConnection(_connStr); await conn.OpenAsync(ct);` (WAL + `Cache=Shared` уже включены в `InitCheckpointSchemaAsync`).
- [ ] `Agent/AgentCore.cs:498` — заменить `_sessions.LogInteraction(...)` на `await _sessions.LogInteractionAsync(..., ct)`.
- [ ] Найти и исправить все остальные вызовы sync-обёрток `SqliteSessionStore` (grep `\.LogInteraction\(`, `\.GetSession\(`, `\.CreateSession\(` без `Async`). Обновить вызовы в `SkillQualityStore`, `SqliteChannelStore`, `SqliteAgentRegistry` если они используют общий connection.
- [ ] `Storage/MemoryStore.cs` — убрать sync-обёртки (`ReadProfile`, `SaveProfile`, и т.д., ~9 методов), оставить только async.
- [ ] `Storage/FileSkillRepository.cs` — заменить синхронные `File.ReadAllText`/`File.WriteAllText` на async-варианты (`ReadAllTextAsync`/`WriteAllTextAsync`).
- [ ] Аудит всех `GetAwaiter().GetResult()` в `Storage/` — убрать; вызвать `*Async` с `await`.
- [ ] Unit-тест: конкурентные записи в `SqliteSessionStore` (10 параллельных `LogInteractionAsync`) — не должно бросать `SQLiteException`; все 10 записей сохраняются.
- [ ] Unit-тест: sync-обёртки больше не существуют (reflection- или source-check: публичные методы `SqliteSessionStore` не содержат не-async вариантов `Log/Get/Create/Update`).
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Storage/SqliteSessionStore.cs, src/agent/Agent/AgentCore.cs, src/agent/Storage/MemoryStore.cs, src/agent/Storage/FileSkillRepository.cs, src/agent/Skills/Quality/SkillQualityStore.cs, src/agent/HerculesBus/Sqlite/SqliteChannelStore.cs, src/agent/HerculesBus/Sqlite/SqliteAgentRegistry.cs

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует: [task_077 — sync-over-async-sweep](task_077.md) (нужен корректный async-паттерн в Storage как референс)

## Risks / Rollback
Connection-per-operation увеличивает число открытых файловых дескрипторов; WAL + shared cache минимизирует overhead. SemaphoreSlim сериализует доступ — acceptable для single-node агента, но ограничивает throughput под нагрузкой. Rollback: вернуть sync-обёртки (git revert).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)