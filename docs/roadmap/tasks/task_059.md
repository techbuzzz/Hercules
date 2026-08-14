# Task 59 — Edge provisioning

**Phase:** 5
**Initiative:** 37
**Status:** done
**Owner:** —
**Slug:** `edge-provisioning`

## Goal
SD-card и Docker images для Raspberry Pi: first-boot Wi-Fi, identity enrolment, API key/cert activation, локальная инициализация storage, secure defaults.

## Acceptance criteria
- [x] `deploy/raspberry-pi/Dockerfile` — multi-stage ARM64 image for .NET 10 agent
- [x] `deploy/raspberry-pi/docker-compose.yml` — container orchestration with healthcheck
- [x] `deploy/raspberry-pi/first-boot.sh` — Wi-Fi provisioning, filesystem expansion, data dir init
- [x] `deploy/raspberry-pi/provision.env` — zeroized template; first-boot fills APikey/LLM credentials
- [x] `src/agent/Edge/EdgeProvisioningService.cs` — first-boot: identity enrolment, cert activation, secure defaults
- [x] `src/agent/Config/EdgeConfig.cs` — EdgeConfig section for appsettings (enrolment URL, Wi-Fi, storage)
- [x] `src/agent/Edge/IEdgeProvisioningService.cs` — service interface
- [x] Program.cs registers EdgeProvisioningService
- [x] `dotnet build` succeeds for Hercules.csproj
- [x] Unit tests cover EdgeProvisioningService

## Scope / Likely files
images/raspberry-pi/, deploy/raspberry-pi/

## Dependencies
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)
- блокирует / опирается на: [task_055 — security-ops](task_055.md)

## Risks / Rollback
Hardware-специфичные баги; CI matrix на реальных устройствах.

## Notes
Implemented: multi-stage ARM64 Dockerfile (Alpine, self-contained .NET 10), docker-compose with healthcheck,
first-boot.sh (Wi-Fi provisioning, identity enrolment, container start), provision.env.template,
EdgeConfig + EdgeProvisioningService (idempotent enrollment, fleet identity activation, cert validation,
secure dir permissions, MAC-based device ID, persisted enrollment state). Enrollment non-fatal on startup.

**Commit:** `7e8da26` — feat(roadmap): complete task 059 - edge provisioning

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
