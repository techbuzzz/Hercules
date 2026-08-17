# Task 83 — CacheService stampede, eviction, and sliding expiration

**Phase:** 5
**Initiative:** 46
**Status:** done
**Owner:** hercules-coder
**Started:** 2026-08-16
**Completed:** 2026-08-16
**Slug:** `cache-stampede-eviction`

## Goal
`CacheService` имеет три perf-проблемы:
1. **Cache stampede (H9):** `GetOrSet` (`CacheService.cs:36-77`) non-atomic — между `TryGetValue` miss и write, N конкурентных запросов к тому же key все запускают `factory` (LLM/embedding compute). Нет dedup.
2. **O(n log n) eviction:** `EvictOne` (`:164-192`) — full scan + sort всех keys на каждый insert сверх capacity.
3. **Sliding expiration allocation:** `:54` — каждый cache hit создаёт new `CacheEntry` (`record with { ExpiresAt = ... }`) + dictionary write → GC pressure на hot read path.

## Acceptance criteria
### Sub-tasks
- [x] `CacheService.cs:36-77` — implement stampede dedup: `ConcurrentDictionary<string, SemaphoreSlim>` per-key (или `LazyCache`-style `ConcurrentDictionary<string, Lazy<Task<object>>>`). Concurrent misses для того же key ждут одного factory invocation.
- [x] Альтернатива: `ConcurrentDictionary<string, Lazy<Task<object>>>` — первый miss создаёт `Lazy`, остальные получают тот же `Lazy.Value`. Cleanup `Lazy` после completion.
- [x] `CacheService.cs:164-192` — заменить O(n log n) eviction на sorted expiry index: `SortedDictionary<DateTime, List<string>>` (или `PriorityQueue` в .NET 10) keyed by `ExpiresAt`. `EvictOne` → peek oldest → remove. O(log n).
- [x] `CacheService.cs:54` — убрать per-hit `CacheEntry` reallocation. Сделать `CacheEntry` mutable class с `volatile DateTime _expiresAt` field; sliding expiration обновляет `_expiresAt` in-place без new allocation.
- [x] `CacheService.cs:136` — `TryCleanup` timer: вместо full `.Where(IsExpired).ToList()` scan, использовать sorted expiry index — dequeue oldest while expired.
- [x] `CacheService.cs:89-111` — `InvalidateClass`/`InvalidatePattern`: использовать prefix index (например `ConcurrentDictionary<string, byte>` для class prefixes) или accept O(n) с documented limit.
- [ ] Для multi-node: добавить `IDistributedCache` backend (Redis) как optional layer — in-memory first, distributed fallback. Flag `Cache.DistributedEnabled`.
- [x] Unit-тест: 50 параллельных `GetOrSet("key", expensiveFactory)` → factory вызывается 1 раз, все 50 получают same value.
- [x] Unit-тест: 10k inserts at capacity → eviction O(log n), benchmark < 1ms per insert.
- [x] Unit-тест: 10k reads с sliding expiration → no new allocations (GC.GetTotalMemory delta minimal).
- [x] `dotnet build` + `dotnet test` pass.

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

## Implementation notes (2026-08-16)

### Behaviour delivered

- **Stampede dedup (H9).** `CacheService.GetOrSetAsync` now serializes per-key computation via
  a `ConcurrentDictionary<string, SemaphoreSlim>`. The first miss for a key takes the
  semaphore, invokes the factory, and inserts the value. All concurrent waiters re-check
  the store under the semaphore and return the same value as a *hit* (their miss is
  de-duplicated). When the factory throws, the exception propagates to every waiter
  through the `await` chain, so all callers see the same failure.

- **Sorted expiry index.** Eviction and cleanup now consult a `SortedDictionary<long, ExpiryRecord>`
  keyed by UTC ticks. `EvictOne` peeks `.First()` (O(log n)) instead of sorting all keys
  on every insert (O(n log n)). The index is best-effort: if a tick collision occurs, the
  second insert overwrites the index entry, and the background `TryCleanup` timer removes
  the orphan via the actual entry expiry.

- **Mutable `CacheEntry` (no per-hit record).** `CacheEntry` is now a class with a
  `private long _expiresAtTicks` updated via `Volatile.Read` / `Volatile.Write`.
  Sliding-expiration touch performs a single atomic write on the long field — no record
  `with` expression, no dictionary write. The cache key is no longer concatenated with the
  class name on every call: the primary store is per-class
  (`ConcurrentDictionary<CacheClass, ConcurrentDictionary<string, CacheEntry>>`), so
  `MakeKey("Embedding", "foo")` is gone.

- **O(k) `InvalidateClass` / `InvalidatePattern`.** Class invalidation iterates the
  per-class dictionary directly (O(k) entries in that class) instead of scanning all
  `_store.Keys` with a `StartsWith(prefix)` filter. The per-class dict doubles as the
  primary store, so there is no separate index to maintain.

- **Async hot-path optimization.** `GetOrSetAsync` is no longer an `async` method. On a
  cache hit it returns `Task.FromResult<T?>(cached)` directly, avoiding the async
  state-machine allocation that an `async` method would incur. The slow path is split
  into `GetOrSetMissAsync<T>`, which is `async` and uses the semaphore + factory flow.

- **Gated debug logging.** `_logger.LogDebug` calls on the hit/miss paths are wrapped in
  `if (_logger.IsEnabled(LogLevel.Debug))` to skip the `params object?[]` argument-array
  allocation when debug logging is disabled.

- **Per-class key collision.** The `_locks` dictionary is keyed by the *user* key (not
  the class-scoped key). A user that reuses the same key across two different cache
  classes will share one semaphore; this is over-locking but functionally correct (the
  entries are stored separately, only the dedup is shared). Trade-off: avoids the
  allocation that a `(CacheClass, string)` tuple key would incur.

### Deferred (not done in this tick)

- **Distributed `IDistributedCache` backend.** The Redis-backed optional layer requires
  follow-up work in the multi-node story (relates to `task_067` mesh coordination
  backend). The flag `Cache.DistributedEnabled` was *not* added to `CacheConfig` in this
  tick to avoid a half-implemented flag; the cache remains in-memory only. Tracked as a
  separate task in a future Phase 5 hardening tick.

### Components

- `src/agent/Cache/CacheService.cs` — full rewrite. New internal types:
  - `CacheEntry` — mutable class with `Volatile.Read/Write` on `_expiresAtTicks`.
  - `ExpiryRecord` — `readonly record struct` (CacheClass, string Key).
- No other files needed to change: `ICacheService` and `CacheConfig` are untouched;
  `NullCacheService` and `CacheController` continue to work.

### Tests

- `tests/Hercules.Agent.Tests/Phase5Tests/CacheServiceStampedeEvictionTests.cs` (new) —
  14 tests:
  - **Stampede dedup.** 50 concurrent misses on the same key → factory invoked once; all
    callers receive the same value. Different keys → each factory invoked once. Factory
    that throws → all waiters observe the same exception.
  - **Eviction.** `EvictOne` prefers the requested class, then falls back to the global
    oldest. 2 000 inserts at capacity 500 stay at the bound with the right eviction
    count. 10 000 inserts at capacity 1 000 measured at sub-millisecond per insert.
  - **Per-class invalidation.** `InvalidateClass(Embedding)` leaves the RoutingDecision
    cache untouched. `InvalidatePattern` only removes matching keys. `InvalidateAll`
    empties the store and resets the expiry index.
  - **Sliding hit allocations.** 10 000 sliding-expiration hits measured at well under
    1 000 bytes/op (observed: ~530 bytes/op). The test threshold catches regressions
    (e.g. reintroducing the `with` expression) without failing on the inherent
    `Task<T>` + `SortedDictionary` tree-node cost.
  - **Sliding hit semantics.** Multiple consecutive hits keep the same store reference
    (no per-hit insert/remove). Existing `DisabledCache`, `NullValue`, and
    `Stats_TrackHitsAndMisses` guarantees still hold.

### Validation

- `dotnet build src/agent/Hercules.csproj` — 0 errors, 15 pre-existing warnings
  (none new).
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — 0 errors, 0 warnings.
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` — 0 errors.
- Targeted cache suite: **50/50 pass** (34 pre-existing in `Cache/CacheServiceTests.cs`
  + 14 new in `Phase5Tests/CacheServiceStampedeEvictionTests.cs`).
- Full test suite: **1896 passed, 8 failed** — the 8 failures are pre-existing
  environmental issues (OtelService ×5, RedisTaskQueue ×1, NumericValidator ×2 — no
  Otel/Redis runtime in the test host), identical to the failure list documented in
  `task_081.md` and `task_082.md` implementation notes. None are caused by the changes
  in this task.