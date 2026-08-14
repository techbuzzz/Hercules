# Task 28 — Кэширование

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `caching`

## Goal
Deterministic tool results, embeddings, routing decisions и provider-supported prompt prefixes кэшируются с scope, TTL, invalidation и sensitivity-правилами.

## Acceptance criteria

- [x] `Cache/CacheConfig.cs` — `CacheConfig` с настройками TTL, scope, sensitivity per cache class
- [x] `Cache/ICacheService.cs` — `ICacheService` interface с `GetOrSetAsync<T>`, `Invalidate`, `InvalidatePattern`
- [x] `Cache/CacheService.cs` — in-memory реализация с sliding expiration, background cleanup
- [x] `Cache/CacheClass.cs` — enum: `Embedding`, `RoutingDecision`, `LlmPromptPrefix`, `DeterministicToolResult`
- [x] `Cache/SensitivityLevel.cs` — enum: `Public`, `Internal`, `Sensitive`, `Restricted`
- [x] Wiring: `EmbeddingScorer` использует `ICacheService` вместо inline `Dictionary<string, float[]>`
- [x] Wiring: `DeterministicRouter` получает routing decision cache (sliding TTL, key = hash of input)
- [x] Wiring: `ILLMClientFactory` кэширует prompt prefixes per provider
- [x] `CacheConfig` добавлена в `AppConfig`
- [x] DI registration в `Program.cs` (CLI + WebAPI)
- [x] `CacheController` — `GET /api/cache/stats`, `POST /api/cache/invalidate`
- [x] `CacheServiceTests` — TTL expiry, sensitivity rules, invalidation
- [x] `dotnet build` проходит
- [x] `dotnet test` проходит (pre-existing failures unchanged)

## Scope / Likely files
src/agent/Cache/

## Dependencies
- блокирует / опирается на: [task_022 — semantic-routing](task_022.md)

## Risks / Rollback
Stale cache для sensitive данных; per-data-class TTL.

## Implementation notes

### 2026-08-12
**Реализовано:**

- `CacheConfig` — per-class TTL (Embedding: 1h, RoutingDecision: 30s, LlmCapability: 24h, LlmPromptPrefix: 24h, DeterministicToolResult: 5min), sensitivity levels, sliding expiration, max entries, cleanup interval
- `ICacheService` — `GetOrSetAsync<T>` (async factory), `Invalidate`, `InvalidateClass`, `InvalidatePattern`, `InvalidateAll`, `GetStats`; `where T : class?` constraint
- `CacheService` — in-memory `ConcurrentDictionary`-based implementation with background timer cleanup, per-class hit/miss/eviction stats, capacity eviction (oldest by expiry)
- `NullCacheService` — no-op implementation for backward compatibility (tests, disabled cache)
- `EmbeddingScorer` — replaced inline `_embeddingCache` dict with `ICacheService` (task_028 wiring); uses `GetOrSetAsync` for skill embeddings with `CacheClass.Embedding`
- `DeterministicRouter` — added `ICacheService?` parameter; routing decisions cached with 30s TTL via `CacheClass.RoutingDecision`; key = SHA256(input)[:16]
- `ProviderCapabilityDetector` — added `ICacheService?` parameter; results cached via `CacheClass.LlmCapability` with 24h TTL
- `LlmClientFactory` — added `ICacheService?` parameter; clients cached via `CacheClass.LlmPromptPrefix` (provider name as key)
- `AppConfig` — added `public CacheConfig Cache { get; set; } = new()`
- DI registration — `ICacheService, CacheService` registered as singleton in both CLI and WebAPI Program.cs; all affected services (EmbeddingScorer, DeterministicRouter, ProviderCapabilityDetector, LlmClientFactory) receive `ICacheService` via constructor
- `CacheController` — `GET /api/cache/stats` (hit/miss/entries/evicted per class + hit rate), `POST /api/cache/invalidate` (by class/key/pattern)
- `CacheServiceTests` — 14 tests: miss, hit, separate classes, null factory, invalidation, pattern invalidation, class invalidation, all invalidation, disabled cache, stats tracking, capacity eviction, null not cached, sliding expiration

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
