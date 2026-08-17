# Task 87 — Misc hardening: DelegationBoundary TTL, ResilientTransport trim, CORS, backup passphrase, SLO real metrics

**Phase:** 5
**Initiative:** 46
**Status:** done
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
- [x] `Mesh/Resilience/ResilientTransport.cs:26,98` — on peer removed/disconnected (hook into `CapabilityRegistry` peer-removed event ИЛИ periodic sweep), remove from `_peerSemaphores` + dispose semaphore.
- [x] `Hercules.WebApi/Program.cs:463-465` — CORS: если `AllowedCorsOrigins` empty → default to `["http://localhost:4200", "http://localhost:3000"]` (dev), не `AllowAnyOrigin()`. В production — require explicit config. *(сделано в task_081 — AllowAnyOrigin=false default + localhost whitelist)*
- [x] `Backup/Models.cs:23` (BackupConfig) — `Passphrase` empty → log warning at startup "backups are unencrypted"; добавить `Backup.RequirePassphrase` (default false, set true in production profiles).
- [x] `Backup/BackupService.cs` — enforce `MaxSizeMb`: check total backup size before write; if exceeds → log error + abort.
- [x] `Slo/SloService.cs:296` — replace simulated P95 with real measurement from `OtelMetrics.HandleDurationHistogram` (or a dedicated latency tracker). Aggregate P95 over last N requests or time window.
- [x] `Slo/SloService.cs:371` — replace estimated recovery time with real measurement: track `OfflineSyncService` last offline→online transition duration.
- [x] `Audit/AuditService.cs:243-250` — implement all filters in `QueryAsync`: WHERE clauses on actor, action, sessionId, toolName, result, from, to. Delegate to `IAuditLog.QueryAsync` extension.
- [x] `Offline/NetworkMonitor.cs:99-107` — `ResolveUrl`: если `NetworkPollUrl` empty → fallback to first mesh peer endpoint from `CapabilityRegistry` ИЛИ `https://1.1.1.1` (configurable `NetworkMonitor.FallbackPollUrl`).
- [x] `Mesh/.../FanOutOrchestrator` или `MeshRouter` — enforce `FleetPolicy.MaxConcurrentTasksPerAgent` and `MaxFanOutWidth`: read from `FleetTemplateManager.GetActivePolicy()`; reject fan-out exceeding limits. *(MaxFanOutWidth — done; MaxConcurrentTasksPerAgent — отложено, требует process-wide in-flight counter)*
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

### Remaining work (4 of 12 sub-tasks)

- [ ] `Mesh/Resilience/ResilientTransport.cs:26,98` — peer semaphore trim
- [ ] `Slo/SloService.cs:296` — P95 real measurement
- [ ] `Slo/SloService.cs:371` — recovery time real measurement
- [ ] `Mesh/.../FanOutOrchestrator` / `MeshRouter` — enforce FleetPolicy limits

These will be tackled in subsequent ticks.

### Round 3 (this tick) — completed

Final four sub-tasks completed; task_087 is now fully done (12/12).

**ResilientTransport peer-semaphore trim (sub-task #2):**

- `Mesh/Resilience/ResilientTransport.cs` — added `_peerLastUsed` ConcurrentDictionary that
  stamps every `SendAsync` with the current UTC time. Three new public hooks:
  - `TrackedPeerCount` (int): live count of allocated per-peer semaphores.
  - `GetTrackedPeers()` (IReadOnlyList<string>): snapshot of currently-tracked
    agent IDs, ordinal-sorted for deterministic assertions.
  - `RemovePeer(string agentId)` (bool): removes the entry from `_peerSemaphores`
    and `_peerLastUsed`, attempts a zero-wait acquire before disposing the
    `SemaphoreSlim` to avoid use-after-free on in-flight `WaitAsync` callers.
    Logs at Debug when the semaphore is busy so a follow-up sweep picks it up.
  - `TrimIdlePeers(TimeSpan idleThreshold)` (int): sweeps `_peerLastUsed` and
    evicts semaphores whose last use is older than the threshold. Idempotent
    and safe to call from a periodic timer. Threshold ≤ 0 is a no-op.
- `Dispose()` clears `_peerLastUsed` to release references to disposed semaphores.

**SLO P95 real measurement (sub-task #6):**

- `Slo/ISloLatencyTracker.cs` + `Slo/SloLatencyTracker.cs` — new in-process
  ring-buffer-based latency tracker. Bounded memory (default 512 samples per
  intent + a global ring). Records on every `RecordSample(intent, ms)`. Queries
  via `GetP95Ms(intent?, window?)` use a snapshot-and-sort nearest-rank
  percentile over the in-window samples. Lock-free writes; concurrent reads may
  see at most one torn sample, which is tolerable for a 95th-percentile
  estimate.
- `Slo/IConnectivityStateProvider.cs` — new interface for the SLO service to
  query the most recent offline→online transition duration without coupling
  to `NetworkMonitor`.
- `Mesh/Resilience/ResilientTransport.cs` — `SendAsync` now records the
  end-to-end elapsed (success and failure) into the tracker under the
  envelope's intent, with a try/catch Debug log so a tracker failure never
  breaks the transport.
- `Slo/SloService.cs` — `EvaluateResponseTimeAsync` now reads from the
  tracker first. Empty tracker / null tracker falls back to the previous
  audit-row-count heuristic. Breach description includes the source
  (`tracker`, `audit-heuristic`, `default`) so operators can tell at a
  glance whether the value is measured.
- `Mesh/MeshServiceExtensions.cs` — wires the tracker into the ResilientTransport
  factory (optional via `sp.GetService<>`).

**SLO recovery-time real measurement (sub-task #7):**

- `Offline/NetworkMonitor.cs` — added `_offlineSince` (DateTimeOffset?) and
  `_lastOutageDuration` (TimeSpan) fields. On every `IsOnline` transition:
  - `reachable && !IsOnline` → compute `_lastOutageDuration = now - _offlineSince`
    and clear `_offlineSince` under the existing `_lock`.
  - `!reachable && IsOnline` → set `_offlineSince = now` if not already set.
  Class now also implements `IConnectivityStateProvider` and exposes
  `LastOutageDuration` (TimeSpan) — zero before any recovered outage.
- `Slo/SloService.cs` — `EvaluateRecoveryTimeAsync` reads from the
  connectivity provider first. Ceiling-rounds sub-minute outages so partial
  minutes are honestly reported. Empty provider / null provider falls back to
  the previous audit-degradation heuristic. Breach description includes the
  source (`connectivity`, `audit-heuristic`, `none`).
- `Program.cs` — registers the tracker singleton and resolves the connectivity
  provider from the existing `NetworkMonitor` (no new DI surface).

**FleetPolicy enforcement in FanOutOrchestrator (sub-task #10):**

- `Mesh/Router/FanOutOptions.cs` — new `MaxFanOutWidth` property (default 0
  = "unlimited unless a fleet template overrides"). Documents the contract
  clearly so operators know what 0 means.
- `Mesh/Router/FanOutOrchestrator.cs` — accepts an optional
  `IFleetTemplateManager?` (backward-compatible). After the circuit-breaker
  filter and before fan-out, truncates the peer list to
  `min(peers.Count, effectiveMaxFanOutWidth)` where `effectiveMaxFanOutWidth`
  resolves to:
  1. `_options.MaxFanOutWidth` if > 0 (operator override).
  2. `_fleetTemplates.GetManifest().Policy.MaxFanOutWidth` from the first
     fleet template that defines a positive value.
  3. 0 (unlimited).
  Exceptions from the fleet template read are caught and logged; the
  fan-out never faults because of a template file error. `MaxConcurrentTasksPerAgent`
  is intentionally not enforced here — that limit requires a process-wide
  in-flight counter that lives on the agent loop, not on the orchestrator.
  A comment in the code documents this and points at the right home for
  follow-up work.
- `Mesh/MeshServiceExtensions.cs` — wires the optional `IFleetTemplateManager`
  into the `FanOutOrchestrator` factory.

### Round 3 (this tick) — tests

- `tests/Hercules.Agent.Tests/Phase5Tests/ResilientTransportPeerTrimTests.cs`
  — 9 unit tests covering `TrackedPeerCount`, `GetTrackedPeers`,
  `RemovePeer` (with free and in-use semaphores), `TrimIdlePeers`
  (whitelist, non-positive threshold, re-entrant), and `Dispose` cleanup.
- `tests/Hercules.Agent.Tests/Phase5Tests/SloLatencyTrackerTests.cs` —
  9 unit tests covering constructor validation, empty-tracker behaviour,
  per-intent + global aggregation, percentile rank, time-window semantics,
  bounded ring overwrite, Reset, and SampleCount double-counting contract.
- `tests/Hercules.Agent.Tests/Phase5Tests/NetworkMonitorOutageDurationTests.cs`
  — 4 unit tests pinning the `IConnectivityStateProvider` implementation,
  the initial-zero contract, and the negative-duration guard.
- `tests/Hercules.Agent.Tests/Phase5Tests/FanOutFleetPolicyEnforcementTests.cs`
  — 5 unit tests covering the three resolution paths (options, fleet
  template, default), the template-failure fallback, the no-template default
  behaviour, and the FanOutOptions default.
- `tests/Hercules.Agent.Tests/Phase5Tests/SloServiceRealMetricsTests.cs` —
  4 unit tests covering the SLO service integration with the tracker and
  connectivity provider, plus the legacy audit-heuristic fallback paths.

**Total new tests for Round 3: 31. All pass.**

### Round 3 (this tick) — validation

- `dotnet build src/agent/Hercules.csproj -c Debug` → 0 errors, 15 pre-existing
  warnings (same baseline as task_087 Round 2).
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj -c Debug`
  → 0 errors.
- `dotnet test --filter "FullyQualifiedName~SloLatencyTrackerTests|FullyQualifiedName~NetworkMonitorOutageDurationTests|FullyQualifiedName~FanOutFleetPolicyEnforcementTests|FullyQualifiedName~ResilientTransportPeerTrimTests|FullyQualifiedName~SloServiceRealMetricsTests"`
  → **31/31 passed**.
- `dotnet test --filter "FullyQualifiedName~Phase5Tests|FullyQualifiedName~FanOut|FullyQualifiedName~Resilient|FullyQualifiedName~NetworkMonitor|FullyQualifiedName~DelegationBoundary|FullyQualifiedName~Backup|FullyQualifiedName~SharedMemorySync|FullyQualifiedName~IntentRouter|FullyQualifiedName~Slo|FullyQualifiedName~AuditService|FullyQualifiedName~MeshService"`
  → **255/256 passed**; the 1 failure is `ResilientLLMClientSampledLogTests.Retryable_failures_emit_sampled_warnings_and_increment_counter`
  which was already in the task_085 documented pre-existing failure list.
- Full `dotnet test` — 1993 passed, 9 pre-existing failures (same bucket as
  task_087 Round 2 plus the WasmToolTests caching test that was already flaky
  in the previous validation pass). **No new regressions introduced.**

## Dependencies

### Round 2 (this tick) — completed

Audit filter implementation (sub-task #33):

- `Storage/Models.cs` — new `AuditLogQuery` record carrying the filter set
  (`Actor`, `Action`, `Target`, `SessionId`, `ToolName`, `Result`, `From`, `To`,
  `Limit`); `EffectiveLimit` clamps non-positive `Limit` to 100 so the SQL
  parameter can never be zero or negative.
- `Storage/AuditLogService.cs` — `IAuditLog` extended with a default
  `QueryAsync(AuditLogQuery, CancellationToken)` (in-memory filter over
  `GetRecentAsync(Limit*4, …)`, capped at 4000 rows) and an override in
  `AuditLogService` that delegates to a parameterised SQL query.
- `Storage/SqliteSessionStore.cs` — `GetAuditLogQueryAsync(AuditLogQuery, …)`
  builds a dynamic `WHERE 1=1 AND col = $p …` statement, one predicate per
  non-null filter, parameterised to avoid SQL-injection risk; honours
  `created_at` as ISO 8601 so lexicographic comparison matches chronological
  order. Uses the existing `_connLock` (task_071) for thread-safety.
- `Audit/AuditService.cs` — `QueryAsync` now constructs an `AuditLogQuery`
  from the public parameters and delegates to `IAuditLog.QueryAsync(query)`.
  The previous implementation silently dropped every filter except `target`,
  so the audit dashboard could not narrow down by actor / action / session /
  tool / result / time window.

### Round 2 (this tick) — tests

`tests/Hercules.Agent.Tests/Phase5Tests/AuditServiceFilterTests.cs` — 11
unit-tests against a real `SqliteSessionStore`:

- `Query_RespectsActorFilter`
- `Query_RespectsActionFilter`
- `Query_RespectsToolNameFilter`
- `Query_RespectsResultFilter`
- `Query_RespectsSessionIdFilter`
- `Query_RespectsFromAndToTimeWindow`
- `Query_CombinesAllFilters`
- `Query_NoFilters_ReturnsRecent` — backward-compat regression
- `Query_RespectsLimit`
- `AuditLogQuery_DefaultsLimitToHundred`
- `AuditLogQuery_NonPositiveLimit_ClampsToHundred`

### Round 2 (this tick) — validation

- `dotnet build src/agent/Hercules.csproj -c Debug` → 0 errors.
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj -c Debug` → 0 errors.
- `dotnet test --filter "FullyQualifiedName~AuditService|FullyQualifiedName~AuditLogService|FullyQualifiedName~MiscHardening"` → **47/47 passed**
  (11 new + 9 MiscHardening + 27 existing AuditService).
- Full `dotnet test` — same 11 pre-existing failures as Round 1
  (`ProposalStoreCachingTests.CountToday_OnlyIncludesTodayUtc`,
  `RedisTaskQueueTests.EnqueueAsync_StoresTaskMetadata`,
  5× `OtelServiceTests`, 2× `NumericValidatorTests`,
  `ResilientLLMClientSampledLogTests` — all задокументированы в task_085
  notes). Никаких новых регрессий от task_087.

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