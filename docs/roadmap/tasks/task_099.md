# Task 99 — System restart protocol

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `system-restart-protocol`
**Studio Stage:** 6

## Goal
Реализовать supervisor-протокол для удалённого перезапуска агента. `POST /api/system/restart` (system key) ставит флаг, Studio/supervisor опрашивает `GET /api/system/restart-pending` и выполняет restart.

## Acceptance criteria
- [ ] `RestartService` — управление restart-флагом
  - `RequestRestart(reason, requestedBy)` → set `restart-requested=true` in runtime-config.json + `restartRequestedAt` timestamp + `restartReason`
  - `IsRestartPending()` → bool (check flag)
  - `ClearRestartRequest()` → set false (после restart)
- [ ] `SystemController` — endpoints:
  - `POST /api/system/restart` (system key) → request restart, returns 202 Accepted
  - `GET /api/system/restart-pending` (system key) → `{pending, requestedAt, reason, requestedBy}`
  - `POST /api/system/restart/clear` (system key) → clear flag
- [ ] Agent не убивает себя сам. Restart выполняет внешний supervisor:
  - Studio (если управляет процессом) → kill + spawn
  - systemd / Windows Service → `Environment.Exit(0)` → service auto-restarts
  - Внешний watcher-процесс
- [ ] Audit log: restart requested, restart completed (by supervisor)
- [ ] Graceful: drain in-flight requests перед exit (если agent сам exit)
- [ ] Unit tests: flag set/clear, pending status
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- task_097 (dual API keys — system key for restart endpoint)
- task_098 (CheckIn/CheckOut — restart должен checkout)

## Scope / Likely files
src/agent/Hercules.WebApi/Controllers/SystemController.cs (extend), src/agent/System/RestartService.cs (new), src/agent/Config/RuntimeConfigStore.cs

## Links
- Studio Stage 6: [../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md](../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md)
- Backlog: [../backlog.md](../backlog.md)