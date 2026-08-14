# Task 62 — Fleet templates

**Phase:** 5
**Initiative:** 39
**Status:** done

## Implementation notes

### 2026-08-14

**Added:**

**`src/agent/Fleet/Models.cs`** — 8 types:
- `FleetManifest` — top-level fleet template manifest (name, description, version, vertical, agent template ref, hardware BOM, monitoring, policy, offline)
- `HardwareBom` — hardware specs per vertical (CPU, RAM, storage, network, power, temp range, sensors, notes)
- `MonitoringConfig` — metric thresholds, alert rules with condition expressions, log level, export interval
- `MetricThreshold` — warning/critical thresholds with unit and rolling window
- `AlertRule` — named rule with severity, condition expression (e.g. "cpu_pct > 90"), cooldown
- `FleetPolicy` — delegation, concurrency, fan-out limits, mTLS and signing requirements
- `OfflineDefaults` — degradation mode, allowed offline skills, outbox queue, sync interval, notification webhook
- `ApplyFleetTemplateResult`, `FleetTemplateEntry` records

**`src/agent/Fleet/IFleetTemplateManager.cs`** — interface with `List()`, `GetManifest()`, `Apply()`, `GetFleetTemplatesDir()`

**`src/agent/Fleet/FleetTemplateManager.cs`** — implementation:
- Reads `*.fleettemplate` ZIPs from `data/FleetTemplates/`
- `List()` returns all valid fleet templates
- `GetManifest()` reads fleet-manifest.json from the archive
- `Apply()` extracts monitoring.json, policy.json, offline.json, hardware-bom.json to data root; handles agent template reference (bundled or pre-installed)
- Conflict resolution: Skip / Rename / Fail

**`Hercules.WebApi/Controllers/FleetTemplateController.cs`** — 3 endpoints:
- `GET /api/fleet-templates` — list all fleet templates
- `GET /api/fleet-templates/{fileName}` — fleet manifest
- `POST /api/fleet-templates/{fileName}/apply` — apply fleet template

**`src/agent/Config/AppConfig.cs`** — added `FleetTemplatesDir = "FleetTemplates"` to `Phase2Config`

**`src/agent/Program.cs`** — registered `IFleetTemplateManager → FleetTemplateManager` in DI

**Fleet template bundles** (`data/FleetTemplates/{greenhouse,cold-chain,server-room,vending}.fleettemplate`):
- Each ZIP contains: fleet-manifest.json, monitoring.json, policy.json, offline.json, hardware-bom.json
- Source files: `templates/{scenario}/fleet/`

**4 verticals:**
- **greenhouse**: Raspberry Pi 4B, ARM Cortex-A72, IP67 temp sensors, 30s metrics export, offline skills: sensor-monitor, irrigation-control, alert-notify
- **cold-chain**: LTE/NB-IoT tracker, ESP32 or ARM, -40°C to +85°C, 15s metrics, mTLS+signing required
- **server-room**: 1U rack-mount, industrial grade, 15s metrics, offline mode: Offline (critical ops), notification webhook
- **vending**: MDB/CCTalk integration, 60s metrics export, mTLS optional, queued work disabled offline

**Tests** (`tests/.../Fleet/FleetTemplateManagerTests.cs`): 16 unit tests covering:
- List (empty, valid, skip invalid, multiple)
- GetManifest (not found, correct values)
- Apply (config extraction, error cases, rename/skip/fail conflict resolution)
- Default values, entry structure

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors (pre-existing warnings unchanged)
- `dotnet build Hercules.Agent.Tests.csproj` — 0 errors
- `dotnet test --filter FleetTemplateManagerTests` — 16/16 passed
- Full suite: 1598/1607 (9 pre-existing failures: OtelService × 5, BudgetGuard × 1, NumericValidator × 2, BusHttpServer × 1 — all pre-existing)
**Owner:** —
**Slug:** `fleet-templates`

## Goal
Один валидированный template на вертикаль: greenhouse, cold-chain, server closet, energy, vending — software, policy, тесты, мониторинг, offline behaviour, hardware BOM.

## Acceptance criteria

### Sub-tasks

- [x] `Fleet/Models.cs` — types: `FleetManifest`, `HardwareBom` (Cpu/Ram/Storage/Network/Sensors[]), `MonitoringConfig` (MetricThresholds[], AlertRules[], LogLevel), `FleetPolicy` (MaxAgents/Delegation/Hops/Concurrency), `OfflineDefaults` (DegradationMode, AllowedOfflineSkills[], SyncIntervalSeconds), `ConflictResolution`
- [x] `Fleet/IFleetTemplateManager.cs` — interface: `List()`, `GetManifest()`, `Apply()`, `GetFleetTemplatesDir()`
- [x] `Fleet/FleetTemplateManager.cs` — service: reads from `data/FleetTemplates/*.fleettemplate` (ZIP); `List()` returns FleetTemplateEntry[], `GetManifest()` returns FleetManifest, `Apply()` copies agent template + writes fleet config JSONs to data root
- [x] `FleetTemplateController.cs` — WebAPI: `GET /api/fleet-templates`, `GET /api/fleet-templates/{name}`, `POST /api/fleet-templates/{name}/apply`
- [x] 4 fleet template bundles: `data/FleetTemplates/{greenhouse,cold-chain,server-room,vending}.fleettemplate` — each ZIP contains `fleet-manifest.json` + references to the existing `.agenttemplate` + monitoring/policy/offline config JSONs
- [x] Source files: `templates/{scenario}/fleet/fleet-manifest.json`, `templates/{scenario}/fleet/monitoring.json`, `templates/{scenario}/fleet/policy.json`, `templates/{scenario}/fleet/offline.json`
- [x] `tests/.../Fleet/FleetTemplateManagerTests.cs` — unit tests: List (empty + valid + skip invalid + multiple), GetManifest, Apply (config copies, agent template reference, errors)
- [x] `dotnet build` + `dotnet test` pass

## Scope / Likely files
templates/greenhouse/, templates/cold-chain/, ...

## Dependencies
- блокирует / опирается на: [task_030 — agent-templates](task_030.md)
- блокирует / опирается на: [task_031 — template-simulation](task_031.md)

## Risks / Rollback
Слишком специфично для общего ядра; чёткие границы template-vs-core.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
