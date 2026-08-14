# Task 79 — Real health checks infrastructure

**Phase:** 5
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `health-checks-infrastructure`

## Goal
`/api/health` (`WebApi/Program.cs:542`) — static stub возвращающий `{status:"healthy"}` без проверки SQLite, LLM, mesh bus, disk. Не пригоден для K8s liveness/readiness probes. Стандартная ASP.NET Core health check инфраструктура (`AddHealthChecks`/`MapHealthChecks`) не используется; нет `IHealthCheck` implementations. Дополнительно `DegradationManager`'s health checks для mesh-bus и skill-registry — stubs (`DegradationManager.cs:228-245`), LLM check не вызывает провайдера (`:165-192`).

## Acceptance criteria
### Sub-tasks
- [ ] `Hercules.WebApi/Program.cs` — добавить `builder.Services.AddHealthChecks()` + `app.MapHealthChecks("/api/health")` + `app.MapHealthChecks("/api/ready", new HealthCheckOptions { Predicate = _ => true })`.
- [ ] Убрать static stub `app.MapGet("/api/health", ...)` (`:542`).
- [ ] Создать `Hercules.WebApi/Health/SqliteHealthCheck.cs` — `IHealthCheck`: проверяет `SELECT 1` на session store connection (или `_conn.State`). Healthy если OK, Unhealthy если exception.
- [ ] Создать `Hercules.WebApi/Health/LlmHealthCheck.cs` — `IHealthCheck`: ping primary LLM provider (lightweight `/models` endpoint or `CompleteAsync` с минимальным prompt). Degraded если fallback chain активен, Unhealthy если все провайдеры недоступны. Timeout 5s.
- [ ] Создать `Hercules.WebApi/Health/MeshBusHealthCheck.cs` — `IHealthCheck`: вызывает `IMeshBus.IsHealthyAsync(ct)`. Для in-process bus — всегда Healthy. Для Redis/Postgres/NATS — real ping.
- [ ] Создать `Hercules.WebApi/Health/DiskSpaceHealthCheck.cs` — `IHealthCheck`: проверяет free disk space на `data/` dir; Degraded если < 1GB, Unhealthy если < 100MB.
- [ ] Создать `Hercules.WebApi/Health/OutboxHealthCheck.cs` — `IHealthCheck`: проверяет `IOutboxStore.GetPendingCountAsync`; Degraded если > threshold (default 1000).
- [ ] Зарегистрировать все health checks: `.AddCheck<SqliteHealthCheck>("sqlite", tags: ["ready"])`, etc. Liveness = process alive (no checks); Readiness = all tagged "ready".
- [ ] `DegradationManager.cs:165-245` — заменить stub checks на real probes: `CheckLlmHealthAsync` → вызвать `LlmHealthCheck.CheckHealthAsync`; `CheckMeshBusHealthAsync` → `IMeshBus.IsHealthyAsync`; `CheckSkillRegistryHealthAsync` → проверить `FileSkillRepository` может list skills.
- [ ] `CapabilityHealthService.cs:88-141` — peers опрашивают `/api/health` (уже делают); теперь получают real status.
- [ ] `appsettings.json` — добавить `HealthChecks.Enabled` (default true), `HealthChecks.LlmPingTimeoutSec` (default 5), `HealthChecks.DiskMinFreeMb` (default 100), `HealthChecks.OutboxMaxPending` (default 1000).
- [ ] `/api/health` liveness: returns 200 if process alive (no checks). `/api/ready` readiness: returns 200 only if all "ready"-tagged checks pass. `/api/health/detail` (auth-gated): returns per-check breakdown.
- [ ] Unit-тест: mock `SqliteConnection` throwing → `SqliteHealthCheck` returns Unhealthy.
- [ ] Unit-тест: `LlmHealthCheck` timeout → Degraded.
- [ ] Integration-тест: `GET /api/health` → 200; `GET /api/ready` → 200 when all healthy, 503 when SQLite down.
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Hercules.WebApi/Program.cs, src/agent/Hercules.WebApi/Health/ (new folder), src/agent/Degradation/DegradationManager.cs, src/agent/Mesh/CapabilityHealthService.cs, src/agent/appsettings.json

## Dependencies
- блокирует / опирается на: [task_061 — local-degradation](task_061.md)
- блокирует / опирается на: [task_070 — backend-profiles-degradation](task_070.md)
- блокирует / опирается на: [task_060 — offline-resilience](task_060.md) (outbox check)

## Risks / Rollback
LlmHealthCheck добавляет нагрузку (ping каждые N сек); configurable interval + timeout. Rollback: вернуть static stub (но K8s probes не работают).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)