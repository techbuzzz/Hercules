# Task 77 — Sync-over-async sweep: ToolPolicyEngine, SloService, Redis/Postgres timers, RolloutController

**Phase:** 6
**Initiative:** 45
**Status:** pending
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
- [ ] `Tools/Policy/ToolPolicyEngine.cs` — конвертировать `Evaluate` в `async Task<ToolPolicyResult>`; заменить `.GetAwaiter().GetResult()` на `await` для `RequestAsync`, `CheckAllPermissionsAsync`. Обновить всех callers.
- [ ] `Slo/SloService.cs` — конвертировать все sync methods (`Evaluate*`, `GetStatus`, `GetSummary`) в `async Task<T>`; заменить `.GetAwaiter().GetResult()` на `await`. 
- [ ] `Slo/SloService.cs:559` — заменить `limit: 100_000` + in-memory `.Count()` на streamed SQL aggregate (`SELECT COUNT(*) FROM audit WHERE ... GROUP BY ...`) или paginated query. Не загружать 100k rows в память.
- [ ] `Slo/SloService.cs:494-534` — `_violations` и `_statusCache` (plain `Dictionary`) защитить `ConcurrentDictionary` или `lock`; async методы не должны race на эти словари.
- [ ] `Mesh/Backends/Redis/RedisTaskQueue.cs:477-543` — `RequeueTimedOutTasks` timer callback: конвертировать в async timer. Использовать `async void` с try/catch ИЛИ `PeriodicTimer` + `await` loop в `BackgroundService`. Pipeline Redis ops через `IBatch` вместо serial round-trips.
- [ ] `Mesh/Backends/Redis/RedisMeshStateStore.cs:440-468` — polling timer: аналогично, async timer + `await`.
- [ ] `Mesh/Backends/Postgres/PostgresMeshStateStore.cs:528-579` — polling timer: async timer + `await`.
- [ ] `Mesh/Backends/Postgres/PostgresTaskQueue.cs:533` — requeue timer: async timer + `await`.
- [ ] `Hercules.WebApi/Controllers/RolloutController.cs:72,94` — конвертировать sync lambdas в `async (req, manager) => { ... await ... }`.
- [ ] `Mesh/Escalation/EscalationService.cs:162` — заменить `ApproveAsync(...).GetAwaiter().GetResult()` на `await ApproveAsync(...)`; метод уже `async Task`.
- [ ] `LLM/LlmClientFactory.cs:29` — `Create` метод: либо сделать `async Task<ILLMClient>` и `await GetOrSetAsync`, либо использовать sync `GetOrAdd` (не async) для cache lookup без blocking.
- [ ] Grep audit: `GetAwaiter\(\)\.GetResult\(\)` в `src/agent/` — должен остаться только в CLI `Program.cs` top-level (acceptable) и в `EdgeProvisioningService` startup (acceptable, low-frequency).
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Tools/Policy/ToolPolicyEngine.cs, src/agent/Slo/SloService.cs, src/agent/Mesh/Backends/Redis/RedisTaskQueue.cs, src/agent/Mesh/Backends/Redis/RedisMeshStateStore.cs, src/agent/Mesh/Backends/Postgres/PostgresMeshStateStore.cs, src/agent/Mesh/Backends/Postgres/PostgresTaskQueue.cs, src/agent/Hercules.WebApi/Controllers/RolloutController.cs, src/agent/Mesh/Escalation/EscalationService.cs, src/agent/LLM/LlmClientFactory.cs

## Dependencies
- блокирует / опирается на: [task_071 — sqlite-thread-safety](task_071.md) (Storage async-паттерн как референс)
- блокирует / опирается на: [task_047 — retry-timeout-breaker](task_047.md)
- блокирует / опирается на: [task_056 — rate-limits-quotas](task_056.md)
- блокирует / опирается на: [task_064 — operational-slos](task_064.md)

## Risks / Rollback
Interface changes (`Evaluate` → `async`, `SloService` methods → `async`) ripple through callers. `async void` timers risky если unhandled exceptions — обязательно try/catch + log. Rollback: вернуть sync wrappers (но starvation останется).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)