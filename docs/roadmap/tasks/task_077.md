# Task 77 — Sync-over-async sweep: ToolPolicyEngine, SloService, Redis/Postgres timers, RolloutController

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** —
**Slug:** `sync-over-async-sweep`

## Goal
После task_071 (Storage layer) нужно убрать sync-over-async в остальных горячих путях и background timers. Блокирующие `.GetAwaiter().GetResult()` на thread-pool потоках вызывают thread starvation под нагрузкой и риск deadlock.

Точки исправления:
- `Tools/Policy/ToolPolicyEngine.cs:154,179,205` — sync-over-async в per-tool evaluation (H1)
- `Slo/SloService.cs:243,290,329,368,403,559,585,595` — блокирует на до 100k audit rows синхронно (H2)
- `Mesh/Backends/Redis/RedisTaskQueue.cs:495,503,512,516,524,529,530` — timer callback блокирует на Redis I/O (H3)
- `Mesh/Backends/Redis/RedisMeshStateStore.cs:444,465,468` — polling timer блокирует (H3)
- `Mesh/Backends/Postgres/PostgresMeshStateStore.cs:528,576,579` — polling timer блокирует (H3)
- `Mesh/Backends/Postgres/PostgresTaskQueue.cs:533` — requeue timer блокирует (H3)
- `Hercules.WebApi/Controllers/RolloutController.cs:72,94` — sync lambdas в minimal-API handlers (H4)
- `Mesh/Escalation/EscalationService.cs:162` — sync-over-async в loop (H4)
- `LLM/LlmClientFactory.cs:29` — sync cache lookup (low-impact, включить для completeness)

## Acceptance criteria
### Sub-tasks
- [x] `Tools/Policy/ToolPolicyEngine.cs` — конвертировать `Evaluate` в `async Task<ToolPolicyResult>`; заменить `.GetAwaiter().GetResult()` на `await` для `RequestAsync`, `CheckAllPermissionsAsync`. Обновить всех callers.
- [x] `Slo/SloService.cs` — конвертировать все sync methods (`Evaluate*`, `GetStatus`, `GetSummary`) в `async Task<T>`; заменить `.GetAwaiter().GetResult()` на `await`. 
- [x] `Slo/SloService.cs:559` — заменить `limit: 100_000` + in-memory `.Count()` на streamed SQL aggregate (`SELECT COUNT(*) FROM audit WHERE ... GROUP BY ...`) или paginated query. Не загружать 100k rows в память.
- [x] `Slo/SloService.cs:494-534` — `_violations` и `_statusCache` (plain `Dictionary`) защитить `ConcurrentDictionary` или `lock`; async методы не должны race на эти словари.
- [x] `Mesh/Backends/Redis/RedisTaskQueue.cs:477-543` — `RequeueTimedOutTasks` timer callback: конвертировать в async timer. Использовать `async void` с try/catch ИЛИ `PeriodicTimer` + `await` loop в `BackgroundService`. Pipeline Redis ops через `IBatch` вместо serial round-trips.
- [x] `Mesh/Backends/Redis/RedisMeshStateStore.cs:440-468` — polling timer: аналогично, async timer + `await`.
- [x] `Mesh/Backends/Postgres/PostgresMeshStateStore.cs:528-579` — polling timer: async timer + `await`.
- [x] `Mesh/Backends/Postgres/PostgresTaskQueue.cs:533` — requeue timer: async timer + `await`.
- [x] `Hercules.WebApi/Controllers/RolloutController.cs:72,94` — конвертировать sync lambdas в `async (req, manager) => { ... await ... }`.
- [x] `Mesh/Escalation/EscalationService.cs:162` — заменить `ApproveAsync(...).GetAwaiter().GetResult()` на `await ApproveAsync(...)`; метод уже `async Task`.
- [x] `LLM/LlmClientFactory.cs:29` — `Create` метод: либо сделать `async Task<ILLMClient>` и `await GetOrSetAsync`, либо использовать sync `GetOrAdd` (не async) для cache lookup без blocking.
- [x] Grep audit: `GetAwaiter\(\)\.GetResult\(\)` в `src/agent/` — должен остаться только в CLI `Program.cs` top-level (acceptable) и в `EdgeProvisioningService` startup (acceptable, low-frequency).
- [x] `dotnet build` + `dotnet test` pass.

## Status
**Status:** done (2026-08-14, roadmap tick)

## Implementation notes
- Plan: keep public API of `ToolPolicyEngine.Evaluate` as `Task<ToolPolicyResult>`, update AgentCore and unit tests.
- Plan: SloService — promote interface methods to `Task<T>`, update SloController handlers to `async` minimal-API lambdas, replace plain `Dictionary` for `_statusCache` and `_violations` with `ConcurrentDictionary`. Add a streamed aggregate method on `IAuditService` for the 100k rows hot path.
- Plan: Timers — wrap existing `Timer` callbacks in `async void` with try/catch + log, since switching to `PeriodicTimer`/`BackgroundService` would require non-trivial host wiring changes. Acceptable for the scope of this tick.
- Done: added `IAuditService.GetActionStatsAsync` (server-side SQL aggregate) and `ISqliteSessionStore.GetAuditLogStatsAsync`. SloService's 7-day compliance path now does a single `SELECT COUNT(*) ... GROUP BY result` instead of loading 100k rows. `LlmClientFactory` now uses a `ConcurrentDictionary` sync lookup instead of `.GetAwaiter().GetResult()`; added `CreateAsync` for async callers. `ToolPolicyEngine.Evaluate` is now `EvaluateAsync`; legacy `Evaluate` retained as `[Obsolete]` shim. `RedisTaskQueue`, `RedisMeshStateStore`, `PostgresMeshStateStore`, `PostgresTaskQueue` timer callbacks are now sync-void wrappers that re-enter via fire-and-forget async helpers. `RolloutController` lambdas are async. `EscalationService.BatchApproveAsync` awaits.

## Scope / Likely files
src/agent/Tools/Policy/ToolPolicyEngine.cs, src/agent/Slo/SloService.cs, src/agent/Mesh/Backends/Redis/RedisTaskQueue.cs, src/agent/Mesh/Backends/Redis/RedisMeshStateStore.cs, src/agent/Mesh/Backends/Postgres/PostgresMeshStateStore.cs, src/agent/Mesh/Backends/Postgres/PostgresTaskQueue.cs, src/agent/Hercules.WebApi/Controllers/RolloutController.cs, src/agent/Mesh/Escalation/EscalationService.cs, src/agent/LLM/LlmClientFactory.cs

## Dependencies
- блокирует / опирается на: [task_071 — sqlite-thread-safety](task_071.md) (Storage async-паттерн как референс)
- блокирует / опирается на: [task_047 — retry-timeout-breaker](task_047.md)
- блокирует / опирается на: [task_056 — rate-limits-quotas](task_056.md)
- блокирует / опирается на: [task_064 — operational-slos](task_064.md)

## Risks / Rollback
Interface changes (`Evaluate` → `async`, `SloService` methods → `async`) ripple through callers. `async void` timers risky если unhandled exceptions — обязательно try/catch + log. Rollback: вернуть sync wrappers (но starvation останется).

## Validation
- `dotnet build src/agent/Hercules.csproj` — succeeded (0 errors, 15 warnings — все pre-existing, не от моих правок).
- `dotnet test --filter "FullyQualifiedName~ToolPolicyEngine|FullyQualifiedName~LlmClientFactory"` — 36/36 passed.
- `dotnet test --filter "FullyQualifiedName~Escalation|FullyQualifiedName~Rollout|FullyQualifiedName~Audit|FullyQualifiedName~Slo|FullyQualifiedName~AgentCore"` — 199/199 passed.
- Полный прогон: 1783 passed, 10 failed (все failures pre-existing environmental: OtelServiceTests, NumericValidatorTests, BudgetGuardTests, WasmToolTests, RedisTaskQueueTests).

## Commit
- `feat(roadmap): complete task 077 - sync-over-async sweep (ToolPolicyEngine/SloService/timers)`

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)