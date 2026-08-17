# Task 72 — QuotaService cleanup fix and distributed quotas

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** —
**Slug:** `quota-cleanup-fix`

## Goal
`QuotaService.CleanOldEntries` (`Quotas/QuotaService.cs:193-206`) сломан: он создаёт новый `ConcurrentBag` под dummy-ключом `bucket.GetHashCode().ToString()` вместо реального ключа basket. Оригинальный basket никогда не очищается и растёт безгранично → memory leak + некорректные счётчики rate-limit со временем. Дополнительно квоты хранятся in-memory и сбрасываются при рестарте процесса; для fleet-развёртываний нужна опциональная распределённая координация через `IMeshStateStore.INCR`.

## Acceptance criteria
### Sub-tasks
- [x] `Quotas/QuotaService.cs:193-206` — исправить `CleanOldEntries`: заменить dummy-key swap на корректный `AddOrUpdate` на реальном ключе basket, ИЛИ перейти на `ConcurrentQueue<DateTime>` + периодический prune по `TryDequeue` пока head < cutoff.
- [x] Добавить периодический background cleanup (timer 60s или `BackgroundService`) который вызывает `CleanOldEntries` для всех baskets, а не только on-demand при insert.
- [x] `Quotas/QuotaService.cs:342` — исправить `MakeStatus`: использовать реальный `QuotaScope` переданный в `GetStatus`, не хардкодить `QuotaScope.Agent`.
- [x] `Quotas/QuotaService.cs:17` — заменить `ConcurrentBag<DateTime>` (stack, O(n) Count) на `ConcurrentQueue<DateTime>` (FIFO, O(1) peek для cleanup).
- [x] Добавить опциональный `DistributedQuotaService` (flag `Quotas.DistributedEnabled` в config): counters через `IMeshStateStore.IncrementAsync($"quota:{scope}:{id}:{bucket}", 1, ttl: windowSec)`, rate-check через `GetAsync` + локальный sliding window. Fallback на in-memory если `IMeshStateStore` недоступен.
- [x] `Config/AppConfig.cs` — добавить `QuotasConfig.DistributedEnabled` (default false), `QuotasConfig.DistributedKeyPrefix` (default "quota:").
- [x] Unit-тест: basket не растёт после 1000 inserts + cleanup → Count <= maxBucketSize.
- [x] Unit-тест: `MakeStatus(Skill, "skill-1")` возвращает `QuotaScope.Skill`, не `Agent`.
- [x] Unit-тест: `DistributedQuotaService` увеличивает counter через mock `IMeshStateStore` и корректно считает rate.
- [x] `dotnet build` + `dotnet test` pass.

## Implementation notes

### 2026-08-14

**`src/agent/Quotas/QuotaService.cs`** — четыре исправления:

1. **ConcurrentBag → ConcurrentQueue** (line 17-22): заменил `ConcurrentDictionary<string, ConcurrentBag<DateTime>>` на `ConcurrentDictionary<string, ConcurrentQueue<DateTime>>`. `ConcurrentBag.Count` — O(n), order undefined, поэтому старый `CleanOldEntries` не мог нормально prune. `ConcurrentQueue` — FIFO с O(1) `Enqueue`/`TryDequeue`/`TryPeek` и тривиальным head-peek для cleanup.

2. **`CleanOldEntries` → `PruneOldEntries`** (line 193-208): новая in-place версия: `while (TryPeek && head < cutoff) TryDequeue`. Никаких dummy keys, никаких подмен references — bucket остаётся той же `ConcurrentQueue`, а ссылки не утекают.

3. **`MakeStatus` signature fix** (line 342): добавлен параметр `QuotaScope scope`; раньше хардкодил `QuotaScope.Agent` для всех entries, из-за чего `GetStatus(Skill, ...)` возвращал `QuotaStatus { Scope = Agent, ... }`. Все вызовы из `GetStatus(scope, ...)` обновлены, чтобы передавать реальный scope.

4. **`SweepAllBuckets`** (new public method): обходит все buckets и прорeзает expired entries. Вызывается периодически из `QuotaCleanupBackgroundService`.

**`src/agent/Quotas/QuotaCleanupBackgroundService.cs`** — new `BackgroundService`. Каждые `Quotas.CleanupIntervalSeconds` (default 60) зовёт `QuotaService.SweepAllBuckets` и логирует количество вычищенных entries. Best-effort: exception в sweep не валит host.

**`src/agent/Quotas/DistributedQuotaService.cs`** — new decorator над `IQuotaService`. При `Quotas.DistributedEnabled = true`:
- `RecordUsage` дополнительно вызывает `IMeshStateStore.IncrementAsync($"quota:{scope}:{scopeId}:{type}", 1, ttl: windowSec)` (fire-and-forget, async);
- `GetRateLimitInfo` читает distributed counter и пересчитывает `Remaining = max(0, limit - distributed)`;
- При недоступности store — fallback на in-memory (warning в лог).

**`src/agent/Mesh/Abstractions/IMeshStateStore.cs`** — добавлен optional параметр `TimeSpan? ttl = null` в `IncrementAsync` (backward compatible, default `null` сохраняет старое поведение). Это нужно для sliding-window семантики distributed counters.

**`src/agent/Mesh/InProcess/InProcessMeshStateStore.cs`** + **Redis/Postgres/Nats backends** — реализована поддержка TTL на `IncrementAsync`:
- InProcess: `ExpiresAt` обновляется на каждом increment (sliding window).
- Redis: после `StringIncrementAsync` зовётся `KeyExpireAsync(prefixedKey, ttl)`.
- Postgres: `expires_at` пишется в UPSERT с `COALESCE(@expiresAt, state.expires_at)`.
- NATS: `ExpiresAt` пробрасывается в `CompareAndSetAsync(key, value, current.Version, ttl)`.

**`src/agent/Config/AppConfig.cs`** — `QuotasConfig` расширен тремя полями:
- `DistributedEnabled` (default `false`) — глобальный feature flag;
- `DistributedKeyPrefix` (default `"quota:"`) — префикс distributed counter keys;
- `CleanupIntervalSeconds` (default `60`) — период periodic sweep.

**`src/agent/Program.cs`** — DI: зарегистрирован `QuotaService` как concrete singleton (для `QuotaCleanupBackgroundService`), а `IQuotaService` резолвится в `DistributedQuotaService` если `DistributedEnabled && IMeshStateStore доступен`, иначе в `QuotaService`. Добавлен `services.AddHostedService<QuotaCleanupBackgroundService>()`.

### Tests
**`tests/Hercules.Agent.Tests/Quotas/QuotaServiceCleanupFixTests.cs`** — 7 тестов:
- `GetStatus_*Scope_*ReportsScope` (4 теста) — проверяют что `MakeStatus` возвращает правильный scope (Skill, User, Tenant, Agent);
- `RecordUsage_AfterManyInserts_AndSweep_BucketStaysBounded` — реплей оригинального бага: 1000 inserts + sweep → bucket ≤ 1;
- `SweepAllBuckets_ReturnsNonZero_AfterExpiry` — sweep действительно вычищает entries;
- `SweepAllBuckets_NoExpiry_ReturnsZero` — sweep не вычищает свежие entries.

**`tests/Hercules.Agent.Tests/Quotas/DistributedQuotaServiceTests.cs`** — 6 тестов:
- `RecordUsage_BumpsDistributedCounter_WhenRateLimitType` — 3 calls → counter = 3 в `InProcessMeshStateStore`;
- `RecordUsage_DoesNotBumpDistributedCounter_ForNonRateLimitType` — TokensPerDay не пишется в distributed store;
- `GetRateLimitInfo_ReadsDistributedCounter_AndComputesRemaining` — pre-seeded counter=80, limit=100 → remaining=20;
- `CheckQuotas_DelegatedToInner` — делегирование работает;
- `Disabled_DoesNotCallStore` — `DistributedEnabled=false` → store не дёргается;
- `StoreThrows_FallsBackToLocal` — store down → local counter всё равно обновляется.

## Validation
- `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors, 17 warnings (pre-existing, не от этой задачи).
- `dotnet test --filter "FullyQualifiedName~Quota"` — 40/40 passed (27 QuotaService + 7 cleanup-fix + 6 distributed).
- Full suite: 1741/1750 (9 pre-existing failures: OtelService×5, BudgetGuard×1, NumericValidator×2, RedisTaskQueue×1).

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