# Task 87 — Misc hardening: DelegationBoundary TTL, ResilientTransport trim, CORS, backup passphrase, SLO real metrics

**Phase:** 6
**Initiative:** 46
**Status:** pending
**Owner:** —
**Slug:** `misc-hardening`

## Goal
Набор medium-severity fixes не вошедших в отдельные tasks:

1. **DelegationBoundaryService TTL (M):** `_chainContexts` (`Mesh/Budget/DelegationBoundaryService.cs:20`) — `ConcurrentDictionary` без eviction; растёт безгранично per root request ID.
2. **ResilientTransport peer semaphore trim (M):** `_peerSemaphores` (`ResilientTransport.cs:26`) — per-peer semaphores никогда не удаляются даже после peer gone.
3. **CORS default (M):** `WebApi/Program.cs:463-465` — `AllowAnyOrigin()` по default.
4. **Backup passphrase default (M):** `BackupConfig.Passphrase = ""` — backups unencrypted by default.
5. **SLO real metrics (M):** `SloService.cs:296` — response-time P95 simulated, not measured. `:371` — recovery time estimated, not measured.
6. **AuditService filters (M):** `AuditService.cs:243-250` — `QueryAsync` ignores actor/action/sessionId/toolName/result/from/to filters.
7. **NetworkMonitor fallback (M):** `NetworkMonitor.cs:99-107` — `ResolveUrl` returns null если `NetworkPollUrl` empty → always offline.
8. **FleetPolicy not enforced (M):** `FleetPolicy.MaxConcurrentTasksPerAgent`/`MaxFanOutWidth` declarative only.
9. **BackupService.MaxSizeMb unused (M):** `BackupConfig.MaxSizeMb=500` declared but never checked.
10. **EscalationService fire-and-forget (M):** `IntentRouter.cs:125` — `_ = _escalationService.EscalateAsync(...)` — exceptions swallowed.
11. **SharedMemorySync silent catch (M):** `SharedMemorySync.cs:356-360` — `catch { /* Skip peer on error */ }` — no logging.

## Acceptance criteria
### Sub-tasks
- [ ] `Mesh/Budget/DelegationBoundaryService.cs:20` — добавить TTL eviction: background timer (60s) удаляет `_chainContexts` entries старше `DelegationBoundaryConfig.ChainContextTtlSec` (default 300). ИЛИ cleanup после `CompleteChain(rootId)`.
- [ ] `Mesh/Resilience/ResilientTransport.cs:26,98` — on peer removed/disconnected (hook into `CapabilityRegistry` peer-removed event ИЛИ periodic sweep), remove from `_peerSemaphores` + dispose semaphore.
- [ ] `Hercules.WebApi/Program.cs:463-465` — CORS: если `AllowedCorsOrigins` empty → default to `["http://localhost:4200", "http://localhost:3000"]` (dev), не `AllowAnyOrigin()`. В production — require explicit config.
- [ ] `Backup/Models.cs:23` (BackupConfig) — `Passphrase` empty → log warning at startup "backups are unencrypted"; добавить `Backup.RequirePassphrase` (default false, set true in production profiles).
- [ ] `Backup/BackupService.cs` — enforce `MaxSizeMb`: check total backup size before write; if exceeds → log error + abort.
- [ ] `Slo/SloService.cs:296` — replace simulated P95 with real measurement from `OtelMetrics.HandleDurationHistogram` (or a dedicated latency tracker). Aggregate P95 over last N requests or time window.
- [ ] `Slo/SloService.cs:371` — replace estimated recovery time with real measurement: track `OfflineSyncService` last offline→online transition duration.
- [ ] `Audit/AuditService.cs:243-250` — implement all filters in `QueryAsync`: WHERE clauses on actor, action, sessionId, toolName, result, from, to. Delegate to `IAuditLog.QueryAsync` extension.
- [ ] `Offline/NetworkMonitor.cs:99-107` — `ResolveUrl`: если `NetworkPollUrl` empty → fallback to first mesh peer endpoint from `CapabilityRegistry` ИЛИ `https://1.1.1.1` (configurable `NetworkMonitor.FallbackPollUrl`).
- [ ] `Mesh/.../FanOutOrchestrator` или `MeshRouter` — enforce `FleetPolicy.MaxConcurrentTasksPerAgent` and `MaxFanOutWidth`: read from `FleetTemplateManager.GetActivePolicy()`; reject fan-out exceeding limits.
- [ ] `Mesh/IntentRouter.cs:125` — replace fire-and-forget with `try { await _escalationService.EscalateAsync(...) } catch (Exception ex) { _logger.LogError(ex, ...) }` ИЛИ `Task.Run` with try/catch + log.
- [ ] `Mesh/SharedMemorySync.cs:356-360` — replace `catch { }` with `catch (Exception ex) { _logger.LogWarning(ex, "Skip peer {PeerId} sync", peerId); }`.
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Mesh/Budget/DelegationBoundaryService.cs, src/agent/Mesh/Resilience/ResilientTransport.cs, src/agent/Hercules.WebApi/Program.cs, src/agent/Backup/BackupService.cs, src/agent/Backup/Models.cs, src/agent/Slo/SloService.cs, src/agent/Audit/AuditService.cs, src/agent/Offline/NetworkMonitor.cs, src/agent/Mesh/Router/FanOutOrchestrator.cs, src/agent/Mesh/IntentRouter.cs, src/agent/Mesh/SharedMemorySync.cs, src/agent/appsettings.json

## Dependencies
- блокирует / опирается на: [task_048 — delegation-boundaries](task_048.md)
- блокирует / опирается на: [task_053 — mesh-dashboard](task_053.md)
- блокирует / опирается на: [task_063 — backup-recovery](task_063.md)
- блокирует / опирается на: [task_064 — operational-slos](task_064.md)
- блокирует / опирается на: [task_014 — audit-privacy](task_014.md)

## Risks / Rollback
Individual fixes independent; rollback per-fix via git revert. CORS tightening может сломать existing web UI deployments — document migration.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)