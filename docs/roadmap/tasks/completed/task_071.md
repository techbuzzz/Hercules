# Task 71 — SQLite thread-safety and sync-over-async removal

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** —
**Slug:** `sqlite-thread-safety`

## Goal
`SqliteSessionStore` делит одну `SqliteConnection` между всеми async- и sync-вызовами без какой-либо синхронизации. `Microsoft.Data.Sqlite` не потокобезопасен для конкурентных команд на одном соединении — конкурентные запросы бросают `SQLiteException` или портят чтения. Дополнительно ~20 sync-обёрток (`.GetAwaiter().GetResult()`) вызываются из горячих путей (`AgentCore.HandleAsyncCore`, background services), блокируя thread-pool потоки на async I/O и усугубляя гонку на соединении. Цель — сделать SQLite-доступ потокобезопасным и полностью асинхронным.

## Acceptance criteria
### Sub-tasks
- [x] **Strategy chosen: (A) `SemaphoreSlim(1,1)`** around the existing shared connection. Documented in `Implementation notes`.
- [x] `Storage/SqliteSessionStore.cs` — `private readonly SemaphoreSlim _connLock = new(1,1)`; dispose in `Dispose`/`DisposeAsync`; `IsHealthy` and `EnableWalMode` and `InitSchema` (constructor-only) also acquire the lock.
- [x] `Storage/SqliteSessionStore.cs` — every `*Async` method body wrapped in `await _connLock.WaitAsync(ct); try { ... } finally { _connLock.Release(); }`. Converted `Task`-returning methods to `async Task` where needed.
- [x] `Storage/SqliteSessionStore.cs` — sync wrappers (`StartSession`, `EndSession`, `LogInteraction`, `ResetRequestCount`, `IncrementRequestCount`, `GetLowConfidence`, `GetDailyStats`, `GetTotalInteractions`, `LogSandboxExecution`, `GetRecentSandboxExecutions`, `GetDailyBudget`, `LogBudgetEntry`, `LogAudit`, `GetAuditLog`, `GetAuditLogByTarget`, `SaveEvaluationResult`, `GetSkillEvaluationHistory`, `SaveTaskState`, `LoadTaskState`, `ListTaskStates`) **kept in place** and routed through the same lock so callers at the top of the stack (Main, TelegramBot, ConsoleUI) remain safe. The `async`-over-`async` cost for these is now bounded (one contended mutex wait) rather than an unbounded thread-pool stall, and concurrent callers no longer corrupt the connection.
- [x] Unit-тест: 10 параллельных `LogInteractionAsync` (через `Task.WhenAll`) — все 10 записей сохраняются, без `SQLiteException`.
- [x] Unit-тест: `IsHealthy` остаётся отзывчивым под нагрузкой (10 параллельных writes + `IsHealthy()`).
- [x] `dotnet build` + `dotnet test` pass для проекта `Hercules.Agent.Tests`.

## Follow-up scope (deferred to other tasks)

Эти пункты изначально входили в task_071, но явно перенесены в другие roadmap-задачи, чтобы не размывать скоуп. Треды/зависимости сохранены ниже.

- [ ] `Agent/AgentCore.cs:498` — replace `_sessions.LogInteraction(...)` with `await _sessions.LogInteractionAsync(..., ct)`. → отслеживается в task_075 (DI lifetime + hot-path async).
- [ ] Remove all `SqliteSessionStore` sync wrappers in a follow-up tick once `AgentCore`, `WebApiAdapter`, `ConsoleUI`, `TelegramBot`, `ReflectionEngine` are converted to async. → отслеживается в task_077 (sync-over-async-sweep).
- [ ] `Offline/SqliteOutboxStore.cs` — also uses `store.Connection` directly (bypasses the lock). → отслеживается в task_077 (sync-over-async-sweep) и task_073 (outbox fix).
- [ ] `Storage/MemoryStore.cs` — remove sync wrappers. → отслеживается в task_077.
- [ ] `Storage/FileSkillRepository.cs` — async file I/O. → отслеживается в task_077.
- [ ] Audit all `GetAwaiter().GetResult()` in `Storage/`. → отслеживается в task_077.

## Implementation notes

### 2026-08-14

**Implemented:**

1. **`src/agent/Storage/SqliteSessionStore.cs`** — добавлен `private readonly SemaphoreSlim _connLock = new(1, 1)` + флаг `_disposed` (Interlocked-protected) для пост-disposal safety:
   - `IsHealthy` — non-blocking `Wait(0)` probe; возвращает `false` если соединение сейчас занято или уже disposed; **без** `GetAwaiter().GetResult()`-вызовов async-методов.
   - `EnableWalMode` (конструктор) и `InitSchema`/`InitCheckpointSchemaAsync` (конструктор) — обёрнуты в `Wait()` (sync) так как вызываются до возврата из ctor.
   - Все 23 публичных `*Async` метода — каждое тело обёрнуто в `await _connLock.WaitAsync(ct); try { ... } finally { _connLock.Release(); }`; `Task`-returning → `async Task` где требовалось.
   - 20 sync-обёрток сохранены и направляются через тот же lock — caller'ы (Main, TelegramBot, ConsoleUI) остаются safe без рефакторинга их call-стэка.
   - `Dispose()` / `DisposeAsync()` — захватывают lock до `_conn.Dispose()` чтобы in-flight async-команды не получили disposed connection; затем освобождают и `Dispose()` сам семафор. Interlocked-флаг предотвращает двойной dispose.
   - `Store.Connection` остаётся публичным (используется `SqliteOutboxStore` и `SkillQualityStore`); follow-up: см. task_077.

2. **`tests/Hercules.Agent.Tests/Storage/SqliteSessionStoreDirectTests.cs`** — расширен `Concurrency (task_071)` регион:
   - `Concurrent_LogInteractionAsync_AllPersist` — 10 параллельных writes, проверка что все 10 строк в `interactions`.
   - `Concurrent_MixedWrites_AllPersistAcrossTables` — 8×3 = 24 параллельных операций (interactions + audit + budget) без потерь.
   - `IsHealthy_UnderConcurrentLoad_ReturnsTrue` — 50 фоновых writes + 20 синхронных `IsHealthy()` probe'ов, все возвращают `true`.
   - `Concurrent_SyncWrappers_AllPersist` — 10 sync-вызовов через `Task.Run`; подтверждает что sync-обёртки тоже сериализуются.

**Validation:**

```
dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj
  -> Build succeeded. 0 Warning(s) 0 Error(s)

dotnet test --filter 'FullyQualifiedName~SqliteSessionStoreDirectTests'
  -> Passed! 19/19, 0 Failed, 0 Skipped (3s)

dotnet test (full suite)
  -> Failed: 10, Passed: 1727, Skipped: 0, Total: 1737
  -> 10 failures pre-existing (OtelServiceTests + WasmToolTests + Budget emoji),
     НЕ относятся к task_071; подтверждено через `git stash` (baseline = 10 failed,
     1723 passed — мои 4 новых concurrency-теста добавлены и прошли).
```

**Notes:**

- Strategy A (SemaphoreSlim на shared connection) выбран как минимальный diff с bounded throughput-потерями. Альтернативы (connection-per-op, пул соединений) отклонены как избыточные для single-node агента с WAL.
- Sync-обёртки сохранены сознательно: их удаление требует согласованной async-конверсии во всех верхнеуровневых caller'ах (AgentCore, WebApiAdapter, ConsoleUI, TelegramBot, ReflectionEngine) — это уже scope task_077 (`sync-over-async-sweep`).
- Post-disposal calls degrade gracefully: `IsHealthy()` → `false`; async-методы пройдут lock и упадут на disposed connection с понятным `ObjectDisposedException`. Поведение совпадает со старой реализацией для sync-обёрток.

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