# Task 79 — Real health checks infrastructure

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** —
**Slug:** `health-checks-infrastructure`

## Goal
`/api/health` (`WebApi/Program.cs:542`) — static stub возвращающий `{status:"healthy"}` без проверки SQLite, LLM, mesh bus, disk. Не пригоден для K8s liveness/readiness probes. Стандартная ASP.NET Core health check инфраструктура (`AddHealthChecks`/`MapHealthChecks`) не используется; нет `IHealthCheck` implementations. Дополнительно `DegradationManager`'s health checks для mesh-bus и skill-registry — stubs (`DegradationManager.cs:228-245`), LLM check не вызывает провайдера (`:165-192`).

## Acceptance criteria
### Sub-tasks
- [x] `Hercules.WebApi/Program.cs` — добавить `builder.Services.AddHealthChecks()` + `app.MapHealthChecks("/api/health")` (liveness) + `app.MapHealthChecks("/api/ready", ...)` (readiness).
- [x] Убрать static stub `app.MapGet("/api/health", ...)`.
- [x] Создать `Health/HealthChecksConfig.cs` — секция `HealthChecks`: `Enabled`, `LlmPingTimeoutSec`, `DiskMinFreeMb`, `DiskDegradedMb`, `OutboxMaxPending`.
- [x] Создать `Health/SqliteHealthCheck.cs` — `IHealthCheck`: `SELECT 1` через `SqliteSessionStore.IsHealthy()`. Healthy если OK, Unhealthy если exception. Tag `ready`.
- [x] Создать `Health/LlmHealthCheck.cs` — `IHealthCheck`: пинг primary LLM provider через `ILLMProviderProbe.CheckAsync` с timeout. Healthy если primary OK, Degraded если только fallback доступен, Unhealthy если всё недоступно. Tag `ready`. Опциональный (если LLM отключён — Healthy).
- [x] Создать `Health/MeshBusHealthCheck.cs` — `IHealthCheck`: `IMeshBus.IsHealthyAsync(ct)`. Tag `ready`. Опциональный (если mesh выключен — Healthy).
- [x] Создать `Health/DiskSpaceHealthCheck.cs` — `IHealthCheck`: `DriveInfo` free space на `StorageConfig.DataRoot`. Degraded < DiskDegradedMb, Unhealthy < DiskMinFreeMb. Tag `ready`.
- [x] Создать `Health/OutboxHealthCheck.cs` — `IHealthCheck`: `IOutboxStore.GetPendingCountAsync`. Degraded > threshold. Tag `ready`. Опциональный (если outbox не зарегистрирован — Healthy).
- [x] Создать `Health/SkillRegistryHealthCheck.cs` — `IHealthCheck`: `FileSkillRepository.LoadAll()` работает без exception. Tag `ready`. Опциональный.
- [x] `/api/health/detail` — JSON-разбивка per check (через `ResponseWriter`); `ApiKeyMiddleware` enforced.
- [x] `DegradationManager.cs` — заменить stub checks: `CheckLlmHealthAsync` использует `ProviderHealthChecker.CheckAsync`; `CheckMeshBusHealthAsync` использует `IMeshBus.IsHealthyAsync`; `CheckSkillRegistryHealthAsync` использует `FileSkillRepository.LoadAll().Count`.
- [x] `CapabilityHealthService.cs:88-141` — peers опрашивают `/api/health` (без изменений), теперь получают real status (liveness HTTP 200).
- [x] `appsettings.json` — добавить секцию `HealthChecks` с дефолтами.
- [x] Зарегистрировать все health checks в `WebApi/Program.cs` через `AddCheck<T>`. Optional checks с `failureStatus: Degraded` для Outbox; остальные Unhealthy.
- [x] Unit-тесты (`tests/Hercules.Agent.Tests/Health/HealthCheckTests.cs`): 17 тестов для всех IHealthCheck + HealthChecksConfig defaults.
- [x] `dotnet build` + `dotnet test` pass.

## Status
**Status:** done (2026-08-16, roadmap tick)

## Implementation notes

- `src/agent/Health/` — новая папка в основном проекте (не WebApi), чтобы тесты могли инстанцировать IHealthCheck без ссылки на WebApi. `HealthCheckResponseWriter` остаётся в `Hercules.WebApi/Health/`, т.к. он использует `HttpContext`.
- `ILLMProviderProbe` / `ProviderHealthCheckerAdapter` — minimal interface поверх `ProviderHealthChecker` (sealed). Позволяет mock'ать LLM health check в юнит-тестах. DI регистрирует адаптер в `WebApi/Program.cs`.
- Liveness: `Predicate = _ => false` → не запускает никакие checks, отвечает `{status: "alive", time}`. Годится для K8s liveness probe.
- Readiness: `Predicate = check => check.Tags.Contains("ready")` → запускает все помеченные checks. K8s readiness probe получает 503 если хотя бы один check не Healthy.
- Detail: `Predicate = _ => true` → полная разбивка. Защищён `ApiKeyMiddleware` (уже гард на `/api/*`).
- `DegradationManager` — добавлен extended ctor с `ProviderHealthChecker?`, `LlmConfig?`, `IMeshBus?`, `FileSkillRepository?` (все optional, backward-compat). Старый ctor сохранён. CLI `Program.cs` использует новый ctor с DI; WebApi использует старый (DegradationManager там не регистрируется — health endpoints берут на себя monitoring).
- `CapabilityHealthService` — без изменений; продолжает опрашивать `/api/health` peer'ов. Liveness endpoint всегда возвращает 200 при живом процессе, что и нужно для capability registry.
- `WebApi/Program.cs` — добавлена секция `HealthChecks` в `appsettings.json`. Добавлена регистрация `IOutboxStore` (ранее была только в CLI) — нужна для OutboxHealthCheck.

## Scope / Likely files
src/agent/Hercules.WebApi/Program.cs, src/agent/Health/ (new folder in main project), src/agent/Hercules.WebApi/Health/HealthCheckResponseWriter.cs, src/agent/Degradation/DegradationManager.cs, src/agent/appsettings.json, src/agent/Program.cs

## Dependencies
- блокирует / опирается на: [task_061 — local-degradation](task_061.md)
- блокирует / опирается на: [task_070 — backend-profiles-degradation](task_070.md)
- блокирует / опирается на: [task_060 — offline-resilience](task_060.md) (outbox check)

## Risks / Rollback
LlmHealthCheck добавляет нагрузку (ping каждые N сек); configurable timeout (5s default). Rollback: вернуть static stub (но K8s probes не работают).

## Validation
- `dotnet build src/agent/Hercules.csproj` — succeeded (0 errors, 15 pre-existing warnings).
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — succeeded (0 errors, 1 pre-existing warning).
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` — succeeded (0 errors).
- `dotnet test --filter "FullyQualifiedName~HealthCheckTests"` — 17/17 passed.
- `dotnet test --filter "FullyQualifiedName~Degradation|FullyQualifiedName~Health"` — 131/131 passed.
- Полный прогон: 1814 passed, 9 failed (все failures pre-existing environmental: `OtelServiceTests` x5, `RedisTaskQueueTests` x1, `NumericValidatorTests` x2, `BudgetGuardTests` x1 — подтверждено в tasks 075-078, не связаны с правкой).

## Commit
- `feat(roadmap): complete task 079 - real health checks infrastructure (liveness + readiness)`

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
