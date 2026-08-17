# Task 98 — CheckIn/CheckOut protocol

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `checkin-checkout-protocol`
**Studio Stage:** 1

## Goal
Реализовать CheckIn/CheckOut протокол: пока одна Studio подключена (contribute key), агент "Checked Out", второе подключение невозможно. System key может подключаться параллельно (read-only monitor). См. [ADR-0005](../EPIC_Hercules_Studio/adr/0005-checkin-checkout.md).

## Acceptance criteria

### Sub-tasks
- [x] `CheckInService` — in-memory state + lock + Timer cleanup
  - `CheckIn(studioId, studioName, role)` → success or 409 if another contribute is active; idempotent re-checkin by same studioId
  - `Heartbeat(studioId, role)` → update LastHeartbeat, returns false if no active session for this studioId
  - `CheckOut(studioId, role)` → release, returns false if no active session
  - `GetStatus()` → `CheckInStatus { CheckedOut, CheckedOutBy, CheckedOutAt, TtlSeconds, Role }`
  - `ForceCheckOut(by)` → system-only call path, audit logged
  - System role: CheckIn/Heartbeat/CheckOut are no-ops (parallel monitor)
  - Background `Timer`: cleanup expired checkins (TTL 60s); `internal CleanupExpired()` for tests
- [x] `SystemController` — Minimal API endpoints, mapped via `app.MapSystem()`:
  - `POST /api/system/checkin` (any auth) → 200 / 409
  - `POST /api/system/checkout` (any auth) → 204 / 404
  - `POST /api/system/checkin/heartbeat` (any auth) → 200 / 404
  - `GET /api/system/checkin/status` (any auth) → 200
  - `POST /api/system/checkin/force` (system, `.RequireSystemRole()`) → 200
- [x] Role check via `HttpContext.Items["ApiKeyRole"]` set by `ApiKeyMiddleware` (task_097). Default `Contribute` если middleware не сработал (для unit-тестов).
- [x] Audit log events: `checkin`, `checkin_reconnect`, `checkin_monitor`, `checkout`, `force_checkout`, `auto_checkout_ttl_expired`. Fire-and-forget с try/catch — audit failure не должен ломать основной flow.
- [x] DI: `CheckInService` зарегистрирован как singleton; `IAuditService` resolve'ится через `IServiceProvider` (audit опционален для тестов).
- [x] Unit tests: checkin success, second-contribute refusal, idempotent re-checkin, system parallel, heartbeat, checkout, force, TTL expiry, dispose safety.
- [x] `dotnet build` + `dotnet test` pass

## Dependencies
- task_097 (dual API keys — нужны роли)

## Scope / Likely files
src/agent/Hercules.WebApi/Controllers/SystemController.cs (new), src/agent/CheckIn/CheckInService.cs (new), src/agent/Hercules.WebApi/Program.cs

## Implementation notes

### Round 1 (this tick) — completed

Реализован CheckIn/CheckOut протокол для Studio (ADR-0005).

**Service (`src/agent/CheckIn/CheckInService.cs`):**
- In-memory state (`CheckInRecord? _active`) под `lock`.
- `CheckIn(studioId, studioName, role)`:
  - `Contribute` role: проверяет, не занят ли агент другим contribute-Studio → 409-style `CheckInResult { Success=false, Error, Status }`. Идемпотентный re-checkin тем же studioId — обновляет heartbeat и имя, не меняет `CheckedInAt`.
  - `System` role: no-op + audit `checkin_monitor`. Не модифицирует state → несколько system могут подключаться параллельно.
- `Heartbeat(studioId, role)`: обновляет `LastHeartbeat`, возвращает false если сессии нет или studioId не совпадает. System role — no-op success.
- `CheckOut(studioId, role)`: освобождает агента, audit `checkout`. System — no-op success.
- `ForceCheckOut(by)`: system-only call path, audit `force_checkout`. Если никого нет — no-op success.
- `GetStatus()`: snapshot `{ CheckedOut, CheckedOutBy, CheckedOutAt, TtlSeconds, Role }`.
- Background `Timer` с интервалом 15s → `CleanupExpired()`: ищет `LastHeartbeat > TTL` (60s по умолчанию) → release + audit `auto_checkout_ttl_expired`.
- `internal CleanupExpired()` — точка входа для тестов (Timer отключается через `cleanupInterval: TimeSpan.Zero`).
- Audit вызовы через `SafeAuditAsync` с try/catch → падающий audit не ломает основной flow.
- `IDisposable` — Timer dispose, после Dispose все операции бросают `ObjectDisposedException`.

**Role-изоляция (`CheckInRole` enum):**
- `CheckInRole` (Contribute=0, System=1) — локальный enum в `Hercules.CheckIn` namespace, чтобы service не зависел от `Hercules.WebApi.Config.ApiKeyRole` (cross-project coupling).
- `SystemController.ResolveRole(HttpContext)` транслирует `ApiKeyRole` → `CheckInRole`.

**Controller (`src/agent/Hercules.WebApi/Controllers/SystemController.cs`):**
- Minimal API через `app.MapGroup("/api/system").WithTags("System")` + `app.MapSystem()` extension.
- 5 endpoints, role-based поведение через service:
  - `POST /api/system/checkin` — 200 / 400 (validation) / 409 (conflict)
  - `POST /api/system/checkout` — 204 / 400 / 404
  - `POST /api/system/checkin/heartbeat` — 200 / 400 / 404
  - `GET /api/system/checkin/status` — 200 (any auth)
  - `POST /api/system/checkin/force` — 200, `RequireSystemRole()` filter
- `WithName("SystemCheckIn")` / `SystemCheckOut` / `SystemCheckInHeartbeat` / `SystemCheckInStatus` / `SystemCheckInForce` — operationId для OpenAPI/Orval.
- 5 endpoints добавлены в root-endpoint list (`/` discovery) в Program.cs.

**Program.cs wiring:**
- `builder.Services.AddSingleton<Hercules.CheckIn.CheckInService>();` (рядом с `ApiKeyStore`, чтобы service registration шли вместе).
- `app.MapSystem();` в блоке domain controllers (после `MapSecurityOps()`, до `MapLlm()`).

**Тесты (`tests/Hercules.Agent.Tests/CheckIn/CheckInServiceTests.cs`) — 25 unit-tests, все passing:**
- Successful first contribute checkin (status.CheckedOut=true)
- Second contribute by different studioId → 409-style `Success=false`
- Idempotent re-checkin same studioId (обновляет имя, не `CheckedInAt`)
- System role parallel (несколько system checkin'ов, state не меняется)
- System checkin then contribute (contribute succeed)
- Heartbeat success / fail (no active / different studioId / system no-op)
- CheckOut success / fail / system no-op
- Force checkOut (with by / without by → default "system")
- Force on empty agent (no-op success, no audit)
- TTL expiry (50ms TTL, internal `CleanupExpired()` → 120ms sleep)
- TTL не убивает при свежем heartbeat
- Status empty initial state
- Validation throws (empty studioId, empty studioName)
- Dispose safety
- Audit failure не ломает CheckIn (mock throws → service still returns success)

**Build & test validation:**
- `dotnet build src/agent/Hercules.csproj` → **exit 0** (0 errors)
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → **exit 0** (0 errors)
- `dotnet test --filter "FullyQualifiedName~CheckIn"` → **25/25 passed**
- `dotnet test --filter "FullyQualifiedName~WebApi"` → **47/47 passed** (no regressions)
- Full suite: 2051/2060 passed. 9 pre-existing failures (NumericValidator, OtelService, RedisTaskQueue, ResilientLLMClientSampledLog) — задокументированы в task_097, требуют внешних сервисов, не связаны с CheckIn/WebApi.

### Notes
- Путь к сервису изменён: `src/agent/System/` → `src/agent/CheckIn/`. Причина: `namespace Hercules.System` ломает неявный `using System;` в основном проекте (BCL `System.Text`/`System.Collections` etc. резолвятся как `Hercules.System.*`). Чистый C# gotcha — namespace не должен shadow'ить BCL.
- `CheckInRole` — локальный enum чтобы избежать cross-project reference (main agent project не имеет доступа к `Hercules.WebApi.Config.ApiKeyRole`).
- TTL/cleanupInterval параметризованы для тестов; прод-дефолты 60s/15s.
- Audit fire-and-forget с try/catch → падающий audit не валит CheckIn flow.
- Manual smoke (`dotnet run` + curl) не выполнен в cron-окружении; покрыт 25 unit-тестами + DI регистрацией.

## Links
- ADR-0005: [../EPIC_Hercules_Studio/adr/0005-checkin-checkout.md](../EPIC_Hercules_Studio/adr/0005-checkin-checkout.md)
- Backlog: [../backlog.md](../backlog.md)