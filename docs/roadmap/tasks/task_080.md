# Task 80 — Graceful shutdown and drain

**Phase:** 5
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `graceful-shutdown-drain`

## Goal
Нет graceful shutdown/drain: `IHostApplicationLifetime` injected в `McpServerHost` но не используется. `LifecycleService.DrainAgentAsync` (`:185-215`) устанавливает `Draining` state но ничего не hooks — новые запросы продолжают приниматься. In-flight запросы и background services отменяются abruptly via `CancellationToken` при shutdown. При K8s rolling deploy in-flight запросы теряются.

## Acceptance criteria
### Sub-tasks
- [ ] `Program.cs` + `WebApi/Program.cs` — зарегистрировать `IHostApplicationLifetime.ApplicationStopping` callback → вызвать `ILifecycleService.DrainAgentAsync(localAgentId, ct)`.
- [ ] `Lifecycle/LifecycleService.cs:185-215` — `DrainAgentAsync` должен: (1) установить `Draining` state, (2) перестать принимать новые запросы (см. ниже), (3) ждать in-flight с timeout (default 30s), (4) cancel remaining, (5) установить `Stopped`.
- [ ] `Agent/AgentCore.cs` — в начале `HandleAsyncCore` проверить `_lifecycle.IsDraining(sessionId)`; если Draining → return 503/degraded response сразу.
- [ ] `Hercules.WebApi/Program.cs` — middleware: если `Draining` → return `503 Service Unavailable` + `Retry-After: 30` header (даёт K8s сигнал подождать).
- [ ] In-flight tracking: `IInFlightTracker` (new) — `Increment(sessionId)`/`Decrement(sessionId)`/`WaitForEmptyAsync(timeout)`. `AgentCore.HandleAsyncCore` оборачивается в `using var tracker = _inFlight.Begin(sessionId)`.
- [ ] `DrainAgentAsync` → `await _inFlight.WaitForEmptyAsync(TimeSpan.FromSeconds(30))` → если timeout, log warning + cancel.
- [ ] `Mcp/McpServerHost.cs:26-39` — убрать unused `_appLifetime` injection ИЛИ использовать его для graceful MCP shutdown.
- [ ] `Offline/OfflineSyncService.cs:158-163` — `OnReconnected` handler: при shutdown (`ApplicationStopping`) не запускать flush. Проверять `ct.IsCancellationRequested` в handler.
- [ ] Background services (`DegradationManager`, `MeshBackendHealthMonitor`, `CapabilityHealthService`, `BackupScheduler`, `CacheService` cleanup timer) — все должны honour `ApplicationStopping` token и complete gracefully (stop timers, flush pending).
- [ ] `Console.CancelKeyPress` (`Program.cs:620-625`) — координировать с `IHostApplicationLifetime.StopApplication()`.
- [ ] `appsettings.json` — `Shutdown.DrainTimeoutSec` (default 30), `Shutdown.CancelInFlightAfterDrain` (default true).
- [ ] Unit-тест: `DrainAgentAsync` → новые `HandleAsync` возвращают 503; in-flight completes normally.
- [ ] Unit-тест: in-flight не завершается за timeout → `DrainAgentAsync` cancels + logs.
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Program.cs, src/agent/Hercules.WebApi/Program.cs, src/agent/Lifecycle/LifecycleService.cs, src/agent/Agent/AgentCore.cs, src/agent/Agent/IInFlightTracker.cs (new), src/agent/Agent/InFlightTracker.cs (new), src/agent/Mcp/McpServerHost.cs, src/agent/Offline/OfflineSyncService.cs, src/agent/appsettings.json

## Dependencies
- блокирует / опирается на: [task_057 — lifecycle-management](task_057.md)
- блокирует / опирается на: [task_075 — di-lifetime-fixes](task_075.md) (session state для in-flight tracking)

## Risks / Rollback
Drain timeout 30s может задержать shutdown; configurable. In-flight tracker добавляет overhead на каждый request (Interlocked Increment/Decrement — minimal). Rollback: убрать ApplicationStopping callback (но in-flight запросы теряются при deploy).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)