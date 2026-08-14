# Task 81 — Kestrel tuning, framework rate limiter, compression, output cache

**Phase:** 5
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `kestrel-rate-limiter-compression`

## Goal
WebAPI использует дефолтные Kestrel limits, custom `RateLimitMiddleware` (только `/api/chat`, без eviction idle IPs → unbounded memory), нет response compression, нет output cache. Дубль `app.MapCache()` (`Program.cs:636-637`). Для production API под нагрузкой это bottleneck.

## Acceptance criteria
### Sub-tasks
- [ ] `Hercules.WebApi/Program.cs` — `builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxConcurrentConnections = 1000; o.Limits.MaxConcurrentUpgradedConnections = 100; o.Limits.MaxRequestBodySize = 4_194_304; o.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2); o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30); })`.
- [ ] Убрать custom `RequestBodyLimitMiddleware` (`Program.cs:492`) если Kestrel `MaxRequestBodySize` покрывает; ИЛИ оставить как defense-in-depth но с real stream-length check.
- [ ] `builder.Services.AddRateLimiter(o => { o.AddFixedWindowLimiter("chat", ...); o.AddConcurrencyLimiter("expensive", ...); })` + `app.UseRateLimiter()`. Применить `/api/chat` → fixed window 30/min per-IP; expensive endpoints (reflection, eval, slo) → concurrency limiter 3 concurrent.
- [ ] Убрать custom `RateLimitMiddleware` (`Auth/RateLimitMiddleware.cs`) — заменить на framework rate limiter. Сохранить `X-RateLimit-*` headers через `OnRejected` callback.
- [ ] `builder.Services.AddResponseCompression(o => { o.EnableForHttps = true; o.Providers.Add<BrotliCompressionProvider>(); o.Providers.Add<GzipCompressionProvider>(); o.MimeTypes = ["application/json", "text/plain", "text/event-stream"]; })` + `app.UseResponseCompression()`.
- [ ] `builder.Services.AddOutputCache(o => { o.AddBasePolicy(b => b.Expire(TimeSpan.FromSeconds(30))); o.AddPolicy("Skills", b => b.Expire(TimeSpan.FromMinutes(5)).Tag("skills")); })` + `app.UseOutputCache()`. Применить к `GET /api/skills`, `GET /api/config` (read-only endpoints).
- [ ] `Program.cs:636-637` — убрать дубль `app.MapCache();`.
- [ ] CORS: `Program.cs:463-465` — заменить `AllowAnyOrigin()` default на whitelist из config `AllowedCorsOrigins`; если empty → разрешить только `localhost` (dev), не `*`.
- [ ] `appsettings.json` — добавить `Kestrel.MaxConcurrentConnections`, `RateLimiting.ChatPerMinute`, `RateLimiting.ExpensiveConcurrency`, `Cors.AllowAnyOrigin` (default false).
- [ ] Unit/Integration-тест: 31st request to `/api/chat` в минуту → 429 + `X-RateLimit-Reset` header.
- [ ] Integration-тест: `GET /api/skills` с `Accept-Encoding: br` → response `Content-Encoding: br`.
- [ ] Integration-тест: 2nd `GET /api/skills` within 5 min → `X-Output-Cache: HIT`.
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Hercules.WebApi/Program.cs, src/agent/Hercules.WebApi/Auth/RateLimitMiddleware.cs (remove), src/agent/Hercules.WebApi/Controllers/* (add output cache attributes), src/agent/appsettings.json

## Dependencies
- блокирует / опирается на: [task_005 — interfaces](task_005.md)
- блокирует / опирается на: [task_056 — rate-limits-quotas](task_056.md)

## Risks / Rollback
Kestrel `MaxConcurrentConnections=1000` может reject traffic под пиком; configurable. Output cache может serve stale data; короткий TTL (5 min) + invalidate on skill update. Rollback: вернуть custom middleware (но bugs останутся).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)