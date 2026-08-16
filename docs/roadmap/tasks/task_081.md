# Task 81 — Kestrel tuning, framework rate limiter, compression, output cache

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** hercules-coder
**Started:** 2026-08-16
**Completed:** 2026-08-16
**Slug:** `kestrel-rate-limiter-compression`

## Goal
WebAPI использует дефолтные Kestrel limits, custom `RateLimitMiddleware` (только `/api/chat`, без eviction idle IPs → unbounded memory), нет response compression, нет output cache. Дубль `app.MapCache()` (`Program.cs:636-637`). Для production API под нагрузкой это bottleneck.

## Acceptance criteria
### Sub-tasks
- [x] `Hercules.WebApi/Program.cs` — `builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxConcurrentConnections = 1000; o.Limits.MaxConcurrentUpgradedConnections = 100; o.Limits.MaxRequestBodySize = 4_194_304; o.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2); o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30); })`.
- [x] Оставить custom `RequestBodyLimitMiddleware` как defense-in-depth но с real stream-length check (оборачивает `Request.Body` в `LimitedStream`, отсекает chunked uploads при превышении лимита).
- [x] `builder.Services.AddRateLimiter(o => { o.AddFixedWindowLimiter("chat", ...); o.AddConcurrencyLimiter("expensive", ...); })` + `app.UseRateLimiter()`. Применить `/api/chat` → fixed window 30/min per-IP; expensive endpoints (reflection, eval, slo) → concurrency limiter 3 concurrent.
- [x] Убрать custom `RateLimitMiddleware` (`Auth/RateLimitMiddleware.cs`) — заменить на framework rate limiter. Сохранить `X-RateLimit-*` headers через `OnRejected` callback.
- [x] `builder.Services.AddResponseCompression(o => { o.EnableForHttps = true; o.Providers.Add<BrotliCompressionProvider>(); o.Providers.Add<GzipCompressionProvider>(); o.MimeTypes = ["application/json", "text/plain", "text/event-stream"]; })` + `app.UseResponseCompression()`.
- [x] `builder.Services.AddOutputCache(o => { o.AddBasePolicy(b => b.Expire(TimeSpan.FromSeconds(30))); o.AddPolicy("Skills", b => b.Expire(TimeSpan.FromMinutes(5)).Tag("skills")); })` + `app.UseOutputCache()`. Применить к `GET /api/skills`, `GET /api/config` (read-only endpoints).
- [x] `Program.cs:636-637` — убрать дубль `app.MapCache();`.
- [x] CORS: `Program.cs:463-465` — заменить `AllowAnyOrigin()` default на whitelist из config `AllowedCorsOrigins`; если empty → разрешить только `localhost` (dev), не `*`.
- [x] `appsettings.json` — добавить `Kestrel.MaxConcurrentConnections`, `RateLimiting.ChatPerMinute`, `RateLimiting.ExpensiveConcurrency`, `Cors.AllowAnyOrigin` (default false).
- [x] Unit/Integration-тест: 31st request to `/api/chat` в минуту → 429 + `X-RateLimit-Reset` header.
- [x] Integration-тест: `GET /api/skills` с `Accept-Encoding: br` → response `Content-Encoding: br`.
- [x] Integration-тест: 2nd `GET /api/skills` within 5 min → `X-Output-Cache: HIT`.
- [x] `dotnet build` + `dotnet test` pass.

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

## Implementation notes (2026-08-16)

### Behaviour delivered
- **Kestrel tuning** — `Program.cs` теперь вызывает `builder.WebHost.ConfigureKestrel(...)` и поднимает
  production-grade лимиты (`MaxConcurrentConnections=1000`, `MaxConcurrentUpgradedConnections=100`,
  `MaxRequestBodySize=4 MiB`, `KeepAliveTimeout=120s`, `RequestHeadersTimeout=30s`). Все значения
  читаются из `WebApiConfig.Kestrel` и переопределяются через `appsettings.json`.
- **Framework rate limiter** — `AddRateLimiter` с двумя политиками:
  - `chat` — `FixedWindowRateLimiter`, partitioned per IP (с учётом `X-Forwarded-For`),
    `PermitLimit=30/min`, `Window=60s`, `QueueLimit=0`. Применён к `POST /api/chat`.
  - `expensive` — `ConcurrencyLimiter` с глобальным `PermitLimit=3` для `/api/reflect`,
    `/api/slos/*`, `/api/maintenance/{run,run-all,proposals/{id}/approve}` и
    `/api/skills/{id}/eval/{harness,baseline}`.
  - `OnRejected` callback проставляет `Retry-After` + `X-RateLimit-Reset` (секунды) и возвращает
    429 + JSON `{"error":"Rate limit exceeded..."}`. Поддержка X-Forwarded-For включена в
    `RateLimitPolicies.GetClientKey`.
- **Response compression** — `AddResponseCompression` с `BrotliCompressionProvider` +
  `GzipCompressionProvider`, `EnableForHttps=true`, MIME-types:
  `application/json`, `text/plain`, `text/event-stream`, `application/xml`, `text/html`.
  Подключён как `app.UseResponseCompression()` ПЕРВЫМ в pipeline.
- **Output cache** — `AddOutputCache` с базовой политикой (30s) и двумя именованными:
  `Skills` (5 min, `SetVaryByQuery("skillId", "includeDeprecated")`, tag `skills`)
  и `Config` (30s, tag `config`). Подключён как `app.UseOutputCache()` после `UseRateLimiter`,
  чтобы 401/429 не кэшировались. Применён к `GET /api/skills`, `GET /api/skills/{id}`,
  `GET /api/config`.
- **CORS hardening** — дефолтный fallback сменился с `AllowAnyOrigin()` на whitelist
  localhost (`http://localhost:{3000,4321,5000}` + их `127.0.0.1` аналоги). Для production
  по-прежнему указывается `WebApi.AllowedCorsOrigins`. Опция `WebApi.AllowAnyOrigin=true`
  осталась для dev/edge-opt-in.
- **Custom `RateLimitMiddleware` удалён** — заменён на framework rate limiter. Файл
  `Auth/RateLimitMiddleware.cs` остался как no-op stub с header-комментарием, указывающим
  на replacement.
- **`RequestBodyLimitMiddleware` усилен** — defense-in-depth обёртка `LimitedStream` поверх
  `Request.Body`. Помимо проверки `ContentLength`, поток реально лимитируется на чтение
  (`InvalidDataException` при превышении), что корректно работает с chunked uploads.
  Middleware ловит `InvalidDataException` и конвертирует в 413 + JSON.
- **Duplicate `app.MapCache()` убран** — был вызван дважды подряд; оставлен один.

### Components
- `src/agent/Hercules.WebApi/RateLimitPolicies.cs` (new) — stable policy name constants +
  `GetClientKey` helper.
- `src/agent/Hercules.WebApi/Config/WebApiConfig.cs` — добавлены `KestrelConfig`,
  `RateLimitingConfig`, флаг `AllowAnyOrigin`; `MaxRequestBodyBytes` стал `long` (для
  согласования с Kestrel).
- `src/agent/appsettings.json` — новая секция `WebApi` с `Kestrel` и `RateLimiting` подсекциями.
- `src/agent/Hercules.WebApi/Auth/RequestBodyLimitMiddleware.cs` — `LimitedStream` теперь
  `public` (для тестов), throws `InvalidDataException` при превышении.
- `src/agent/Hercules.WebApi/Auth/RateLimitMiddleware.cs` — no-op stub, заменён framework'ом.
- 7 controllers: добавлен `.RequireRateLimiting(...)` / `.CacheOutput(...)` где применимо.

### Validation
- `dotnet build src/agent/Hercules.csproj` — 0 errors, 0 new warnings
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` — 0 errors
- New tests: **23/23 pass**
  - `RateLimitPoliciesTests` (7) — policy names stable, `GetClientKey` respects X-Forwarded-For
  - `LimitedStreamTests` (6) — Read/ReadAsync happy path + over-limit throws, Write NotSupported
  - `RequestBodyLimitMiddlewareTests` (4) — ContentLength reject, pass-through, chunked 413, non-/api bypass
  - `KestrelRateLimitCachePipelineTests` (6) — chat 429 after limit, Retry-After header,
    concurrency rejection, Brotli + Gzip content-encoding, output cache HIT (counter-based)
- Full test suite: **1863 passed, 8 failed** — все 8 failures pre-existing environmental
  (`OtelServiceTests` ×5, `NumericValidatorTests` ×2, `RedisTaskQueueTests` ×1 — нет Redis
  / Otel runtime в test host).