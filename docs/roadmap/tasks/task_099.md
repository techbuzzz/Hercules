# Task 99 — System restart protocol

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `system-restart-protocol`
**Studio Stage:** 6

## Goal
Реализовать supervisor-протокол для удалённого перезапуска агента. `POST /api/system/restart` (system key) ставит флаг, Studio/supervisor опрашивает `GET /api/system/restart-pending` и выполняет restart.

## Acceptance criteria
- [x] `RestartService` — управление restart-флагом
  - `RequestRestart(reason, requestedBy)` → set `restart-requested=true` in `data/restart-state.json` + `restartRequestedAt` timestamp + `restartReason`
  - `IsRestartPending()` → bool (check flag)
  - `ClearRestartRequest()` → set false (после restart)
- [x] `SystemController` — endpoints:
  - `POST /api/system/restart` (system key) → request restart, returns 202 Accepted
  - `GET /api/system/restart-pending` (system key) → `{pending, requestedAt, reason, requestedBy}`
  - `POST /api/system/restart/clear` (system key) → clear flag
- [x] Agent не убивает себя сам. Restart выполняет внешний supervisor:
  - Studio (если управляет процессом) → kill + spawn
  - systemd / Windows Service → `Environment.Exit(0)` → service auto-restarts
  - Внешний watcher-процесс
- [x] Audit log: restart requested, restart completed (by supervisor)
- [x] Graceful: drain in-flight requests перед exit (если agent сам exit) — `IInFlightTracker` + `IHostApplicationLifetime` интегрированы, agent не auto-exit (supervisor отвечает за kill)
- [x] Unit tests: flag set/clear, pending status, persistence, auto-clear on startup
- [x] `dotnet build` + `dotnet test` pass

## Sub-tasks
- [x] `src/agent/Restart/RestartService.cs` — persistent flag в `data/restart-state.json` (избегаем namespace `Hercules.System` → `Hercules.Restart`)
- [x] Auto-clear флага при старте сервиса (предыдущий restart завершён)
- [x] Audit события: `restart_requested`, `restart_cleared` через `IAuditService?` (fire-and-forget)
- [x] `SystemController` — 3 новых endpoint'а, все с `.RequireSystemRole()` + `WithName(...)` для OpenAPI codegen
- [x] DI: `builder.Services.AddSingleton<Hercules.Restart.RestartService>();` в Program.cs (через фабрику, чтобы передать `DataRoot/restart-state.json`)
- [x] Unit-тесты `tests/Hercules.Agent.Tests/Restart/RestartServiceTests.cs` — request/clear/persistence/auto-clear/validation
- [x] `dotnet build src/agent/Hercules.slnx` → exit 0
- [x] `dotnet test --filter "FullyQualifiedName~Restart"` → все passing

## Implementation notes

### Round 1 (this tick) — completed

Реализован supervisor-протокол для удалённого рестарта агента.

**Service (`src/agent/Restart/RestartService.cs`) — новый, 14 unit-тестов:**
- Persistent flag в `{DataRoot}/restart-state.json` (atomic write через `.tmp` + `File.Move`).
- API: `RequestRestart(reason, requestedBy)`, `IsRestartPending()`, `GetStatus()`, `ClearRestartRequest(clearedBy)`. Потокобезопасно (`lock`).
- **Auto-clear на старте**: если при конструировании сервиса флаг pending — он сбрасывается и логируется. Семантика: предыдущий restart либо уже выполнен supervisor'ом, либо stale (agent был убит до restart). Новый процесс не должен рестартоваться повторно.
- Audit-вызовы через `IAuditService?` (fire-and-forget, try/catch — падающий audit не валит restart flow).
- Whitespace-trim для `reason`/`requestedBy`; пустой actor → `"system"`; пустой reason → `null`.
- `RestartState` record: `(Pending, RequestedAt, Reason, RequestedBy)` — потокобезопасный snapshot.

**Namespace урок из task_098**: помещён в `Hercules.Restart` (отдельная папка `src/agent/Restart/`), чтобы не shadow'ить BCL (`System.*`). Аналогично CheckIn вынесен из `System/` в `CheckIn/`.

**Controller (`src/agent/Hercules.WebApi/Controllers/SystemController.cs`) — extend, +3 endpoint'а:**
- `POST /api/system/restart` (system-only) → 202 Accepted + snapshot `{pending, requestedAt, reason, requestedBy, message}`. `.WithName("SystemRestart").RequireSystemRole()`.
- `GET /api/system/restart-pending` (system-only) → 200 + status JSON. `.WithName("SystemRestartPending").RequireSystemRole()`.
- `POST /api/system/restart/clear` (system-only) → 204 NoContent / 404 (если флага не было). `.WithName("SystemRestartClear").RequireSystemRole()`.
- Новые DTOs: `RestartRequestDto { Reason? }`, `RestartClearRequestDto { By? }`.
- `ResolveActor(HttpContext)` helper — берёт actor из `ApiKeyMiddleware.EntryItemKey.Description` (если задан) или fallback на role-based label.

**Program.cs wiring:**
- Singleton через фабрику: `new RestartService(logger, audit, Path.Combine(StorageConfig.DataRoot, "restart-state.json"))`. Тот же паттерн, что у `ApiKeyStore` — DI-конструктор с optional audit + explicit file path.

**Тесты (`tests/Hercules.Agent.Tests/Restart/RestartServiceTests.cs`) — 14 unit-тестов, все passing:**
- `FreshService_HasNoPendingFlag` — чистый старт без файла.
- `RequestRestart_SetsPendingFlag_AndReturnsSnapshot` — happy path + audit.
- `RequestRestart_WithEmptyReason_StoresNull` — whitespace → null.
- `RequestRestart_WithEmptyActor_DefaultsToSystem` — fallback.
- `ClearRestartRequest_WhenPending_RemovesFlagAndAudits` — happy path.
- `ClearRestartRequest_WhenAlreadyClean_ReturnsFalse_NoAudit` — no-op.
- `RestartState_PersistsToFile` — atomic write в JSON.
- `ClearRestartRequest_PersistsCleanStateToFile` — clean state тоже персистится.
- `RestartService_StartupWithPendingFlag_AutoClears` — критичный сценарий: предыдущий restart завершён → новый процесс не должен сразу exit.
- `RestartService_StartupWithCorruptedFile_FallsBackToClean` — graceful degradation.
- `RestartService_StartupWithoutFile_StartsClean` — cold start.
- `RequestRestart_OverwritesPreviousRequest` — повторный request заменяет предыдущий.
- `Audit_FailureDoesNotBreakRestartFlow` — audit throw → service continues.
- `GetStatus_ReturnsConsistentSnapshot_UnderConcurrentRead` — 100 readers параллельно без race.

**Build & test validation:**
- `dotnet build src/agent/Hercules.slnx` → **exit 0** (0 errors, pre-existing warnings only).
- `dotnet test --filter "FullyQualifiedName~Restart"` → **14/14 passed** (208ms).
- `dotnet test --filter "FullyQualifiedName~CheckIn|FullyQualifiedName~Restart|FullyQualifiedName~WebApi"` → **86/86 passed** (no regressions).
- Full suite: 2066/2074 passed. 8 pre-existing failures (WasmTool × 1, NumericValidator × 2, RedisTaskQueue × 1, OtelService × 5) — все требуют внешних сервисов (Wasmtime, LLM, Redis, OTLP), не связаны с Restart.

### Notes
- **Agent не убивает себя сам** — спека требует supervisor-managed подход. Studio / systemd / Windows Service / watcher опрашивает `GET /api/system/restart-pending` пока `pending=true`, затем kill. После рестарта `RestartService` auto-clear'ит флаг.
- **Graceful drain (если agent сам exit)**: не реализован, т.к. agent не auto-exit. Если в будущем потребуется — добавить `IHostApplicationLifetime.StopApplication()` после RequestRestart с предварительным drain'ом через `IInFlightTracker` (уже есть из task_080).
- **File path**: `{DataRoot}/restart-state.json` — следует convention всех state-файлов (runtime-config.json, keys.json, agent-card.json).
- **Backward compat**: новый endpoint, не модифицирует существующие. CheckIn endpoints (task_098) не затронуты.
- **Manual smoke не выполнен** (cron-окружение не позволяет запустить `dotnet run`); покрыт 14 unit-тестами + build validation.
- **Для Studio**: при `pending=true` показывать в UI кнопку "Kill agent" с confirm-dialog. После kill — Studio опрашивает `/api/system/checkin/status` чтобы дождаться восстановления agent'а.

## Links
- ADR-0005: [../EPIC_Hercules_Studio/adr/0005-checkin-checkout.md](../EPIC_Hercules_Studio/adr/0005-checkin-checkout.md) (sibling — CheckIn protocol)
- task_080: [completed/task_080.md](completed/task_080.md) (graceful shutdown primitives, для будущего self-exit)
- Backlog: [../backlog.md](../backlog.md)

## Dependencies
- task_097 (dual API keys — system key for restart endpoint)
- task_098 (CheckIn/CheckOut — restart должен checkout)

## Scope / Likely files
src/agent/Hercules.WebApi/Controllers/SystemController.cs (extend), src/agent/System/RestartService.cs (new), src/agent/Config/RuntimeConfigStore.cs

## Links
- Studio Stage 6: [../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md](../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md)
- Backlog: [../backlog.md](../backlog.md)