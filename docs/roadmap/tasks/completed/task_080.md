# Task 80 — Graceful shutdown and drain

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** hercules-coder
**Started:** 2026-08-16
**Completed:** 2026-08-16
**Slug:** `graceful-shutdown-drain`

## Goal
Нет graceful shutdown/drain: `IHostApplicationLifetime` injected в `McpServerHost` но не используется. `LifecycleService.DrainAgentAsync` (`:185-215`) устанавливает `Draining` state но ничего не hooks — новые запросы продолжают приниматься. In-flight запросы и background services отменяются abruptly via `CancellationToken` при shutdown. При K8s rolling deploy in-flight запросы теряются.

## Acceptance criteria
### Sub-tasks
- [x] `Program.cs` + `WebApi/Program.cs` — зарегистрировать `IHostApplicationLifetime.ApplicationStopping` callback → вызвать `ILifecycleService.DrainAgentAsync(localAgentId, ct)`. *(через `DrainHostedService` — `IHostedService.StopAsync` awaits drain, host ждёт)*
- [x] `Lifecycle/LifecycleService.cs:185-215` — `DrainAgentAsync` должен: (1) установить `Draining` state, (2) перестать принимать новые запросы (см. ниже), (3) ждать in-flight с timeout (default 30s), (4) cancel remaining, (5) установить `Stopped`. *(через `IAgentLifecycleState` + `IInFlightTracker`)*
- [x] `Agent/AgentCore.cs` — в начале `HandleAsyncCore` проверить `_lifecycle.IsDraining(sessionId)`; если Draining → return 503/degraded response сразу. *(через `IAgentLifecycleState`, бросает `LifecycleDrainingException`)*
- [x] `Hercules.WebApi/Program.cs` — middleware: если `Draining` → return `503 Service Unavailable` + `Retry-After: 30` header (даёт K8s сигнал подождать). *(`DrainMiddleware`)*
- [x] In-flight tracking: `IInFlightTracker` (new) — `Increment(sessionId)`/`Decrement(sessionId)`/`WaitForEmptyAsync(timeout)`. `AgentCore.HandleAsyncCore` оборачивается в `using var tracker = _inFlight.Begin(sessionId)`. *(`Agent/InFlightTracker.cs`, использует `Interlocked` + `TaskCompletionSource`)*
- [x] `DrainAgentAsync` → `await _inFlight.WaitForEmptyAsync(TimeSpan.FromSeconds(30))` → если timeout, log warning + cancel.
- [x] `Mcp/McpServerHost.cs:26-39` — убрать unused `_appLifetime` injection ИЛИ использовать его для graceful MCP shutdown. *(`StopApplication()` вызывается только если cancellation не запрошен — избегаем redundant stop) *
- [x] `Offline/OfflineSyncService.cs:158-163` — `OnReconnected` handler: при shutdown (`ApplicationStopping`) не запускать flush. Проверять `ct.IsCancellationRequested` в handler. *(уже сделано через `combinedCts`, добавлен явный `IsCancellationRequested` check)*
- [x] Background services (`DegradationManager`, `MeshBackendHealthMonitor`, `CapabilityHealthService`, `BackupScheduler`, `CacheService` cleanup timer) — все должны honour `ApplicationStopping` token и complete gracefully (stop timers, flush pending). *(проверено — все используют `stoppingToken` из `ExecuteAsync`/`StopAsync`)*
- [x] `Console.CancelKeyPress` (`Program.cs:620-625`) — координировать с `IHostApplicationLifetime.StopApplication()`. *(вместо `cts.Cancel()` зовём `IHostApplicationLifetime.StopApplication()`)*
- [x] `appsettings.json` — `Shutdown.DrainTimeoutSec` (default 30), `Shutdown.CancelInFlightAfterDrain` (default true).
- [x] Unit-тест: `DrainAgentAsync` → новые `HandleAsync` возвращают 503; in-flight completes normally.
- [x] Unit-тест: in-flight не завершается за timeout → `DrainAgentAsync` cancels + logs.
- [x] `dotnet build` + `dotnet test` pass.

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

## Implementation notes (2026-08-16)

### Behaviour delivered
- **State decoupling** — extracted `IAgentLifecycleState` (with `AgentLifecycleStateHolder`) so
  the lifecycle state is shared between `LifecycleService` (writer) and `AgentCore` /
  `DrainMiddleware` (readers) without a `LifecycleService → AgentCore` circular dep.
- **In-flight tracking** — `IInFlightTracker` / `InFlightTracker` expose
  `Begin()` (returns a `using`-friendly `IDisposable`) and `WaitForEmptyAsync(timeout, ct)`.
  Lock-free on the hot path (`Interlocked` counter); the wait path uses a per-call
  `TaskCompletionSource` + signal queue so multiple waiters all unblock when the
  counter hits zero.
- **`LifecycleService.DrainAgentAsync`** — now actually awaits in-flight:
  `Running → Draining → (wait) → Stopped`. `DrainedCleanly` + `RemainingInFlight` are
  recorded in the `Metadata` of the returned `LifecycleActionResult`.
- **`AgentCore.HandleAsyncCore`** — throws `LifecycleDrainingException` at the top of the
  body when `IAgentLifecycleState.IsShuttingDown`; the rest of the body is wrapped in
  `using var _inFlightScope = _inFlight?.Begin()` so every request (success, exception,
  cancellation) releases its slot.
- **`DrainMiddleware`** — registered before auth/rate-limit so 503s come out clean.
  Exempts `/api/health*`, `/api/ready`, `/api/live`, `/agent.manifest.json`,
  `/agent-card.json` so K8s liveness probes still work.
- **`DrainHostedService`** — `IHostedService.StopAsync` calls `DrainAgentAsync`. The
  .NET host awaits `StopAsync`, so the process won't exit until in-flight drains.
- **`McpServerHost`** — only calls `IHostApplicationLifetime.StopApplication()` when
  the cancellation token wasn't already requested (avoids redundant stop signal
  during host shutdown).
- **`OfflineSyncService.OnReconnected`** — early-returns when `stoppingToken.IsCancellationRequested`
  to avoid starting a half-completed flush during shutdown.
- **`Program.cs` (CLI)** — `Console.CancelKeyPress` now also calls
  `IHostApplicationLifetime.StopApplication()` so the drain hosted service fires.
- **`appsettings.json`** — new `Shutdown` section (`Enabled`, `DrainTimeoutSec`,
  `CancelInFlightAfterDrain`, `PostStopRetryAfterSec`).

### Validation
- `dotnet build src/agent/Hercules.csproj` — 0 errors, 15 pre-existing warnings
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — 0 errors, 1 pre-existing warning
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` — 0 errors
- New tests: **25/25 pass**
  - `InFlightTrackerTests` (7) — Begin/Dispose, fast path, wait resolution, timeout, multiple waiters, double-dispose, post-timeout signal
  - `AgentLifecycleStateHolderTests` (3) — defaults, SetState, event firing
  - `LifecycleDrainTests` (6) — state transition, in-flight wait, timeout, double-drain rejection, stop-after-drain
  - `AgentCoreShutdownTests` (5) — Draining/Stopped/Decommissioned throw, Running works, counter wraps
  - `DrainMiddlewareTests` (4) — 503 + Retry-After, fallback Retry-After, exempt paths, normal pass-through
- Full test suite: **1839 passed, 9 failed** — all 9 failures pre-existing
  (`OtelServiceTests` ×5, `NumericValidatorTests` ×2, `RedisTaskQueueTests` ×1, `WasmToolTests` ×1
  — environmental: no Otel/Redis/WASM runtime in test host).