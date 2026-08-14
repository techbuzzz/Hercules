# Task 57 — Управление жизненным циклом

**Phase:** 5
**Initiative:** 36
**Status:** done
**Owner:** —
**Slug:** `lifecycle-management`

## Goal
CLI и API: inventory, start, stop, drain, update, canary, health check, rollback, decommissioning агентов и skill packages.

## Acceptance criteria
- [x] Create `src/agent/Lifecycle/ILifecycleService.cs` — interface with lifecycle operations
- [x] Create `src/agent/Lifecycle/Models.cs` — DTOs: AgentInventoryEntry, LifecycleAction, HealthCheckResult, CanaryResult, RollbackResult
- [x] Create `src/agent/Lifecycle/LifecycleService.cs` — implementation with in-process agent lifecycle management
- [x] Create `src/agent/Hercules.WebApi/Controllers/LifecycleController.cs` — web API endpoints
- [x] Add lifecycle commands to `src/agent/CLI/ConsoleUI.cs`
- [x] Register lifecycle services in DI (CLI and WebAPI Program.cs)
- [x] Add unit tests in `tests/Hercules.Agent.Tests/Lifecycle/LifecycleServiceTests.cs`
- [x] `dotnet build` — 0 errors; `dotnet test` — all lifecycle tests pass

## Scope / Likely files
src/agent/CLI/Commands/AgentLifecycleCommand.cs, src/agent/Hercules.WebApi/Controllers/LifecycleController.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)

## Risks / Rollback
Случайный downtime; canary + auto-rollback при health-degradation.

## Implementation notes

**Date:** 2026-08-14

**`src/agent/Lifecycle/Models.cs`** — DTOs: `AgentInventoryEntry`, `SkillPackageInventoryEntry`, `AgentResourceUsage`, `LifecycleActionResult`, `HealthCheckResult`, `CanaryResult`, `RollbackResult`, `LifecycleAction` enum.

**`src/agent/Lifecycle/ILifecycleService.cs`** — Interface with 10 methods: agent inventory, start/stop/drain/decommission, health check, package update/canary/promote/rollback.

**`src/agent/Lifecycle/LifecycleService.cs`** — In-process lifecycle management:
- Local agent state machine: Running → Draining → Stopped → Decommissioned
- Skill package deployment state tracked in-memory per packageId
- Health check: local (direct) and remote (via ITransport)
- Canary deploy with configurable traffic percentage
- Rollback for local agents requires restart (documents the limitation)

**`src/agent/Hercules.WebApi/Controllers/LifecycleController.cs`** — 11 endpoints:
- `GET /api/lifecycle/inventory` (agents or packages)
- `GET /api/lifecycle/health`
- `POST /api/lifecycle/agent/{id}/start|stop|drain|decommission|rollback`
- `POST /api/lifecycle/packages/{id}/update|canary|promote|rollback`

**`src/agent/CLI/ConsoleUI.cs`** — Added `/lifecycle` command with 11 sub-commands:
- inventory, health, start, stop, drain, decommission, rollback
- update, canary, promote, rollback-pkg

**`src/agent/Program.cs` + `src/agent/Hercules.WebApi/Program.cs`** — Registered `ILifecycleService` in DI.

**Tests:** `tests/Hercules.Agent.Tests/Lifecycle/LifecycleServiceTests.cs` — 17 unit tests (all pass).

**Validation:**
- `dotnet build src/agent/Hercules.csproj` — 0 errors
- `dotnet build tests/.../Hercules.Agent.Tests.csproj` — 0 errors
- `dotnet test --filter "LifecycleServiceTests"` — 17/17 passed
- `dotnet test` — 1512 passed, 9 pre-existing failures (OtelServiceTests)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
