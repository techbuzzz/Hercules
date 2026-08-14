# Task 83 — CacheService stampede, eviction, and sliding expiration

**Phase:** 6
**Initiative:** 46
**Status:** pending
**Owner:** —
**Slug:** `cache-stampede-eviction`

## Goal
`CacheService` имеет три perf-проблемы:
1. **Cache stampede (H9):** `GetOrSet` (`CacheService.cs:36-77`) non-atomic — между `TryGetValue` miss и write, N конкурентных запросов к тому же key все запускают `factory` (LLM/embedding compute). Нет dedup.
2. **O(n log n) eviction:** `EvictOne` (`:164-192`) — full scan + sort всех keys на каждый insert сверх capacity.
3. **Sliding expiration allocation:** `:54` — каждый cache hit создаёт new `CacheEntry` (`record with { ExpiresAt = ... }`) + dictionary write → GC pressure на hot read path.

## Acceptance criteria
### Sub-tasks
- [ ] `CacheService.cs:36-77` — implement stampede dedup: `ConcurrentDictionary<string, SemaphoreSlim>` per-key (или `LazyCache`-style `ConcurrentDictionary<string, Lazy<Task<object>>>`). Concurrent misses для того же key ждут одного factory invocation.
- [ ] Альтернатива: `ConcurrentDictionary<string, Lazy<Task<object>>>` — первый miss создаёт `Lazy`, остальные получают тот же `Lazy.Value`. Cleanup `Lazy` после completion.
- [ ] `CacheService.cs:164-192` — заменить O(n log n) eviction на sorted expiry index: `SortedDictionary<DateTime, List<string>>` (или `PriorityQueue` в .NET 10) keyed by `ExpiresAt`. `EvictOne` → peek oldest → remove. O(log n).
- [ ] `CacheService.cs:54` — убрать per-hit `CacheEntry` reallocation. Сделать `CacheEntry` mutable class с `volatile DateTime _expiresAt` field; sliding expiration обновляет `_expiresAt` in-place без new allocation.
- [ ] `CacheService.cs:136` — `TryCleanup` timer: вместо full `.Where(IsExpired).ToList()` scan, использовать sorted expiry index — dequeue oldest while expired.
- [ ] `CacheService.cs:89-111` — `InvalidateClass`/`InvalidatePattern`: использовать prefix index (например `ConcurrentDictionary<string, byte>` для class prefixes) или accept O(n) с documented limit.
- [ ] Для multi-node: добавить `IDistributedCache` backend (Redis) как optional layer — in-memory first, distributed fallback. Flag `Cache.DistributedEnabled`.
- [ ] Unit-тест: 50 параллельных `GetOrSet("key", expensiveFactory)` → factory вызывается 1 раз, все 50 получают same value.
- [ ] Unit-тест: 10k inserts at capacity → eviction O(log n), benchmark < 1ms per insert.
- [ ] Unit-тест: 10k reads с sliding expiration → no new allocations (GC.GetTotalMemory delta minimal).
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Cache/CacheService.cs, src/agent/Cache/CacheEntry.cs (new, mutable), src/agent/Cache/DistributedCacheService.cs (new, optional), src/agent/Config/AppConfig.cs, src/agent/appsettings.json

## Dependencies
- блокирует / опирается на: [task_028 — caching](task_028.md)
- опционально опирается на: [task_067 — redis-coordination-backend](task_067.md)

## Risks / Rollback
`Lazy<Task>` dedup может deadlock если factory re-enters cache. Stampede dedup добавляет `await` на concurrent misses. Rollback: вернуть non-atomic GetOrSet (но stampede останется).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)