# Task 72 — QuotaService cleanup fix and distributed quotas

**Phase:** 6
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `quota-cleanup-fix`

## Goal
`QuotaService.CleanOldEntries` (`Quotas/QuotaService.cs:193-206`) сломан: он создаёт новый `ConcurrentBag` под dummy-ключом `bucket.GetHashCode().ToString()` вместо реального ключа basket. Оригинальный basket никогда не очищается и растёт безгранично → memory leak + некорректные счётчики rate-limit со временем. Дополнительно квоты хранятся in-memory и сбрасываются при рестарте процесса; для fleet-развёртываний нужна опциональная распределённая координация через `IMeshStateStore.INCR`.

## Acceptance criteria
### Sub-tasks
- [ ] `Quotas/QuotaService.cs:193-206` — исправить `CleanOldEntries`: заменить dummy-key swap на корректный `AddOrUpdate` на реальном ключе basket, ИЛИ перейти на `ConcurrentQueue<DateTime>` + периодический prune по `TryDequeue` пока head < cutoff.
- [ ] Добавить периодический background cleanup (timer 60s или `BackgroundService`) который вызывает `CleanOldEntries` для всех baskets, а не только on-demand при insert.
- [ ] `Quotas/QuotaService.cs:342` — исправить `MakeStatus`: использовать реальный `QuotaScope` переданный в `GetStatus`, не хардкодить `QuotaScope.Agent`.
- [ ] `Quotas/QuotaService.cs:17` — заменить `ConcurrentBag<DateTime>` (stack, O(n) Count) на `ConcurrentQueue<DateTime>` (FIFO, O(1) peek для cleanup).
- [ ] Добавить опциональный `DistributedQuotaService` (flag `Quotas.DistributedEnabled` в config): counters через `IMeshStateStore.IncrementAsync($"quota:{scope}:{id}:{bucket}", 1, ttl: windowSec)`, rate-check через `GetAsync` + локальный sliding window. Fallback на in-memory если `IMeshStateStore` недоступен.
- [ ] `Config/AppConfig.cs` — добавить `QuotasConfig.DistributedEnabled` (default false), `QuotasConfig.DistributedKeyPrefix` (default "quota:").
- [ ] Unit-тест: basket не растёт после 1000 inserts + cleanup → Count <= maxBucketSize.
- [ ] Unit-тест: `MakeStatus(Skill, "skill-1")` возвращает `QuotaScope.Skill`, не `Agent`.
- [ ] Unit-тест: `DistributedQuotaService` увеличивает counter через mock `IMeshStateStore` и корректно считает rate.
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Quotas/QuotaService.cs, src/agent/Quotas/DistributedQuotaService.cs (new), src/agent/Quotas/Models.cs, src/agent/Config/AppConfig.cs, src/agent/Program.cs

## Dependencies
- блокирует / опирается на: [task_056 — rate-limits-quotas](task_056.md)
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md) (для distributed)
- опционально опирается на: [task_067 — redis-coordination-backend](task_067.md), [task_069 — postgres-shared-state](task_069.md)

## Risks / Rollback
Distributed quotas добавляют сетевую задержку на каждый `CheckQuotas`; измерить p99 и сделать fallback на in-memory при недоступности store. Rollback: `DistributedEnabled=false`.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)