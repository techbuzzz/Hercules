# Task 70 — Backend-профили и деградация

**Phase:** 4
**Initiative:** 30
**Status:** done
**Owner:** —
**Slug:** `backend-profiles-degradation`

## Goal
Профили развёртывания объявляют, какой mesh используется: только локальный SQLite, Redis/Valkey, NATS, PostgreSQL или комбинации. Если бэкенд становится недоступен, агенты деградируют в local-only режим или прекращают приём новых делегаций по policy, а не падают молча. Состояние деградации наблюдаемо.

## Acceptance criteria

### Sub-tasks

- [x] `Mesh/Profiles/MeshProfile.cs` — types: `MeshBackendProfile` enum (Local, Redis, Nats, Postgres, Hybrid), `MeshProfileDefinition` (name, description, backends map, constraints, degradationPolicy), `MeshBackendConfig` (kind, connectionString/hosts, enabled, healthCheckIntervalSec, timeoutSec, maxRetries), `DegradationPolicy` (mode: FailSilent/DegradeToLocal/RefuseDelegations, alertWebhook, maxDegradedSeconds before full-stop)
- [x] `Mesh/Profiles/MeshProfileLoader.cs` — reads profiles from config section; resolves effective profile by name; validates required backends are available; logs profile activation
- [x] `Mesh/Backend/IMeshBackendHealthMonitor.cs` — interface: `GetBackendStatuses()`, `SubscribeToChanges(Func<BackendHealthChangedEvent, Task>)`, `BackendHealthChangedEvent` record
- [x] `Mesh/Backend/MeshBackendHealthMonitor.cs` — BackgroundService; periodically calls `IMeshBus.IsHealthyAsync`, `ITaskQueue.IsHealthyAsync`, `IMeshStateStore.IsHealthyAsync`; transitions states (Healthy → Degraded → Unavailable); calls registered callbacks; emits OTel metrics
- [x] `Config/AppConfig.cs` — add `MeshProfilesConfig` with ActiveProfile, ProfilesDir, HealthCheckIntervalSec, EnableDegradationAlerts
- [x] `Config/AppConfig.cs` — add `MeshProfiles` property to `MeshConfig`
- [x] `Mesh/MeshServiceExtensions.cs` — register `MeshProfilesConfig` and `IMeshBackendHealthMonitor` in DI; log active profile at startup
- [x] `Program.cs` — register `MeshProfilesConfig` and `IMeshBackendHealthMonitor` in DI
- [x] `Hercules.WebApi/Controllers/MeshProfileController.cs` — endpoints: `GET /api/mesh/profiles`, `GET /api/mesh/profiles/{name}`, `GET /api/mesh/profiles/{name}/backends`, `GET /api/mesh/backend-status` (live health), `GET /api/mesh/backend-status/{backend}` (per-backend health)
- [x] `tests/.../Mesh/Profiles/MeshProfileLoaderTests.cs` — unit tests: load valid profile, local default profile, profile not found, effective backends resolution
- [x] `tests/.../Mesh/Backend/MeshBackendHealthMonitorTests.cs` — unit tests: all healthy → Degraded → Unavailable transitions, callback invoked, metrics recorded
- [x] `dotnet build` + `dotnet test` pass

## Scope / Likely files
src/agent/Mesh/Profiles/, src/agent/Mesh/Profiles/MeshProfileLoader.cs, src/agent/Mesh/Degradation/HealthMonitor.cs

## Dependencies
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md)
- блокирует / опирается на: [task_065 — mesh-observability](task_065.md)
- блокирует / опирается на: [task_067 — redis-coordination-backend](task_067.md)
- блокирует / опирается на: [task_068 — nats-jetstream-transport](task_068.md)
- блокирует / опирается на: [task_069 — postgres-shared-state](task_069.md)

## Risks / Rollback
Тихое падение в degraded-режим без видимости; явный mode flag + alerting + policy-driven refuse-when-stale.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)

## Implementation notes

**`src/agent/Mesh/Profiles/MeshProfile.cs`** — 13 types: `MeshBackendProfile` enum (Local/Redis/Nats/Postgres/Hybrid), `MeshBackendConfig`, `DegradationPolicy`, `DegradationMode`, `MeshProfileDefinition`, `MeshProfileConstraints`, `MeshBackendHealthStatus`, `BackendHealthState`, `BackendHealthChangedEvent`.

**`src/agent/Mesh/Profiles/MeshProfileLoader.cs`** — loads profiles from config + `.meshprofile.json` files; built-in "local" profile always available; `GetActiveProfile()`, `GetEffectiveBackend()` for per-role resolution.

**`src/agent/Mesh/Backend/IMeshBackendHealthMonitor.cs`** — interface: `GetBackendStatuses()`, `SubscribeToChanges()`, `CheckAllAsync()`.

**`src/agent/Mesh/Backend/MeshBackendHealthMonitor.cs`** — BackgroundService; in-process backends always Healthy (no external calls); external backends monitored on interval; transitions: Healthy→Degraded→Unavailable with consecutive failure tracking; OTel metrics via `IMeshObservabilityService`; subscriber callbacks fire on background thread.

**`Config/AppConfig.cs`** — added `MeshProfilesConfig` and `AppConfig.MeshProfiles` property.

**`Config/AppConfig.cs`** — added `using Hercules.Mesh.Profiles;` (added to `Profiles` dict property).

**`Mesh/MeshServiceExtensions.cs`** — registered `MeshProfilesConfig` and `IMeshBackendHealthMonitor` in DI.

**`Program.cs`** — registered `appConfig.MeshProfiles`.

**`Hercules.WebApi/Controllers/MeshProfileController.cs`** — 5 endpoints: `GET /api/mesh/profiles`, `GET /api/mesh/profiles/{name}`, `GET /api/mesh/profiles/{name}/backends`, `GET /api/mesh/backend-status`, `GET /api/mesh/backend-status/{role}`.

**Tests** (`tests/.../Mesh/Profiles/MeshProfileLoaderTests.cs`, `tests/.../Mesh/Backend/MeshBackendHealthMonitorTests.cs`): 24 unit tests covering profile loading, effective backend resolution, state transitions, callbacks, timeout handling, and subscription lifecycle.

## Validation

- `dotnet build Hercules.csproj -c Release` — 0 new errors (pre-existing warnings unchanged)
- `dotnet build Hercules.WebApi.csproj -c Release` — 0 new errors
- `dotnet build Hercules.Agent.Tests.csproj -c Release` — 0 new errors
- `dotnet test --filter MeshProfileLoader` — 10/10 passed
- `dotnet test --filter MeshBackendHealthMonitor` — 14/14 passed
- Full suite: 1646/1655 (9 pre-existing failures: OtelService×5, BudgetGuard×1, NumericValidator×2, WasmTool×1)
