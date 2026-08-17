# Task 87 — Misc hardening: DelegationBoundary TTL, ResilientTransport trim, CORS, backup passphrase, SLO real metrics

**Phase:** 5
**Initiative:** 46
**Status:** in_progress
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
- [x] `Mesh/Budget/DelegationBoundaryService.cs:20` — добавить TTL eviction: background timer (60s) удаляет `_chainContexts` entries старше `DelegationBoundaryConfig.ChainContextTtlSec` (default 300). ИЛИ cleanup после `CompleteChain(rootId)`.
- [ ] `Mesh/Resilience/ResilientTransport.cs:26,98` — on peer removed/disconnected (hook into `CapabilityRegistry` peer-removed event ИЛИ periodic sweep), remove from `_peerSemaphores` + dispose semaphore.
- [x] `Hercules.WebApi/Program.cs:463-465` — CORS: если `AllowedCorsOrigins` empty → default to `["http://localhost:4200", "http://localhost:3000"]` (dev), не `AllowAnyOrigin()`. В production — require explicit config. *(сделано в task_081 — AllowAnyOrigin=false default + localhost whitelist)*
- [x] `Backup/Models.cs:23` (BackupConfig) — `Passphrase` empty → log warning at startup "backups are unencrypted"; добавить `Backup.RequirePassphrase` (default false, set true in production profiles).
- [x] `Backup/BackupService.cs` — enforce `MaxSizeMb`: check total backup size before write; if exceeds → log error + abort.
- [ ] `Slo/SloService.cs:296` — replace simulated P95 with real measurement from `OtelMetrics.HandleDurationHistogram` (or a dedicated latency tracker). Aggregate P95 over last N requests or time window.
- [ ] `Slo/SloService.cs:371` — replace estimated recovery time with real measurement: track `OfflineSyncService` last offline→online transition duration.
- [ ] `Audit/AuditService.cs:243-250` — implement all filters in `QueryAsync`: WHERE clauses on actor, action, sessionId, toolName, result, from, to. Delegate to `IAuditLog.QueryAsync` extension.
- [x] `Offline/NetworkMonitor.cs:99-107` — `ResolveUrl`: если `NetworkPollUrl` empty → fallback to first mesh peer endpoint from `CapabilityRegistry` ИЛИ `https://1.1.1.1` (configurable `NetworkMonitor.FallbackPollUrl`).
- [ ] `Mesh/.../FanOutOrchestrator` или `MeshRouter` — enforce `FleetPolicy.MaxConcurrentTasksPerAgent` and `MaxFanOutWidth`: read from `FleetTemplateManager.GetActivePolicy()`; reject fan-out exceeding limits.
- [x] `Mesh/IntentRouter.cs:125` — replace fire-and-forget with `try { await _escalationService.EscalateAsync(...) } catch (Exception ex) { _logger.LogError(ex, ...) }` ИЛИ `Task.Run` with try/catch + log.
- [x] `Mesh/SharedMemorySync.cs:356-360` — replace `catch { }` with `catch (Exception ex) { _logger.LogWarning(ex, "Skip peer {PeerId} sync", peerId); }`.
- [x] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Mesh/Budget/DelegationBoundaryService.cs, src/agent/Mesh/Resilience/ResilientTransport.cs, src/agent/Hercules.WebApi/Program.cs, src/agent/Backup/BackupService.cs, src/agent/Backup/Models.cs, src/agent/Slo/SloService.cs, src/agent/Audit/AuditService.cs, src/agent/Offline/NetworkMonitor.cs, src/agent/Mesh/Router/FanOutOrchestrator.cs, src/agent/Mesh/IntentRouter.cs, src/agent/Mesh/SharedMemorySync.cs, src/agent/appsettings.json

## Implementation notes

### Round 1 (this tick) — completed

Hardening-фиксы из перечисленных 12 sub-tasks, реализованные в этом тике (6 из 12):

- `Mesh/SharedMemorySync.cs:356-360` — `catch { }` → `catch (Exception ex) { _logger.LogWarning(ex, "Skip peer {PeerId} on sync error ({ErrorType})", peer.AgentId, ex.GetType().Name); }`.
- `Backup/Models.cs` — `BackupConfig.RequirePassphrase` (default false) добавлен.
- `Backup/BackupScheduler.cs:ExecuteAsync` — startup-предупреждение/ошибка при пустом `Passphrase`. Логика: `LogError` если `RequirePassphrase=true`, иначе `LogWarning`.
- `Backup/BackupService.cs:CreateBackupAsync` — после сборки `finalBytes` добавлена проверка `finalBytes.Length > _config.MaxSizeMb * 1024 * 1024`. При превышении: `LogError` + возврат `BackupResult` с пустым `ArchivePath` и warning в списке, без записи файла.
- `Offline/OfflineSyncConfig.cs` — `NetworkFallbackPollUrl` (default `"https://1.1.1.1"`).
- `Offline/NetworkMonitor.cs:ResolveUrl` — fallback chain: `NetworkPollUrl` → `NetworkFallbackPollUrl` → `null`.
- `Mesh/IntentRouter.cs` — добавлен optional `ILogger<IntentRouter>?` (default `NullLogger`). Fire-and-forget `_ = _escalationService.EscalateAsync(escCtx, ct)` заменён на `_ = EscalateSafelyAsync(escCtx, ct, requestId)`, который ловит `Exception` (кроме `OperationCanceledException` на shutdown) и пишет `LogError`. DI wiring не сломан — параметр опциональный.
- `Mesh/Budget/DelegationBoundaryService.cs` — добавлен `DelegationBoundaryConfig.ChainContextTtlSec` (default 300) + публичный метод `CleanupExpiredChainContexts()`. Опportunistic вызов из `RecordHopCompletion` когда `_chainContexts.Count > 64`.
- `Config/AppConfig.cs` — добавлено поле `ChainContextTtlSec` в `DelegationBoundaryConfig`.
- `appsettings.json` — добавлен `DelegationBoundaries.ChainContextTtlSec = 300`.

### Round 1 (this tick) — tests

`tests/Hercules.Agent.Tests/Phase5Tests/MiscHardeningTests.cs` — 9 unit-тестов:
- `BackupConfig_RequirePassphrase_DefaultsToFalse`
- `OfflineSyncConfig_NetworkFallbackPollUrl_HasSafeDefault`
- `DelegationBoundaryConfig_ChainContextTtlSec_DefaultsTo300`
- `NetworkMonitor_ResolveUrl_FallsBackToFallbackPollUrl_WhenPrimaryEmpty`
- `NetworkMonitor_ResolveUrl_ReturnsNull_WhenBothEmpty`
- `NetworkMonitor_ResolveUrl_PrefersExplicitPollUrl`
- `DelegationBoundaryService_CleanupExpiredChainContexts_EvictsStale`
- `DelegationBoundaryService_CleanupExpiredChainContexts_NoOpWhenTtlDisabled`
- `DelegationBoundaryService_CleanupExpiredChainContexts_KeepsFreshEntries`

### Validation

- `dotnet build src/agent/Hercules.csproj -c Debug` → 0 errors, 15 pre-existing warnings (same as baseline).
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj -c Debug` → 0 errors.
- `dotnet test --filter "FullyQualifiedName~SharedMemorySyncTests|FullyQualifiedName~Backup|FullyQualifiedName~NetworkMonitor|FullyQualifiedName~IntentRouter|FullyQualifiedName~DelegationBoundary|FullyQualifiedName~MiscHardening"` → **79/79 passed**.
- Полный прогон `dotnet test` показывает 9 pre-existing failures (NumericValidatorTests, OtelServiceTests, RedisTaskQueueTests.EnqueueAsync_StoresTaskMetadata, ResilientLLMClientSampledLogTests — задокументированы в task_085 notes). Новых регрессий от task_087 нет.

### Remaining work (6 of 12 sub-tasks)

- [ ] `Mesh/Resilience/ResilientTransport.cs:26,98` — peer semaphore trim
- [ ] `Slo/SloService.cs:296` — P95 real measurement
- [ ] `Slo/SloService.cs:371` — recovery time real measurement
- [ ] `Audit/AuditService.cs:243-250` — filter implementation in QueryAsync
- [ ] `Mesh/.../FanOutOrchestrator` / `MeshRouter` — enforce FleetPolicy limits

These will be tackled in subsequent ticks.

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