# Task 59 — Edge provisioning

**Phase:** 5
**Initiative:** 37
**Status:** in_progress
**Owner:** —
**Slug:** `edge-provisioning`

## Goal
SD-card и Docker images для Raspberry Pi: first-boot Wi-Fi, identity enrolment, API key/cert activation, локальная инициализация storage, secure defaults.

## Acceptance criteria
- [ ] `deploy/raspberry-pi/Dockerfile` — multi-stage ARM64 image for .NET 10 agent
- [ ] `deploy/raspberry-pi/docker-compose.yml` — container orchestration with healthcheck
- [ ] `deploy/raspberry-pi/first-boot.sh` — Wi-Fi provisioning, filesystem expansion, data dir init
- [ ] `deploy/raspberry-pi/provision.env` — zeroized template; first-boot fills APikey/LLM credentials
- [ ] `src/agent/Edge/EdgeProvisioningService.cs` — first-boot: identity enrolment, cert activation, secure defaults
- [ ] `src/agent/Config/EdgeConfig.cs` — EdgeConfig section for appsettings (enrolment URL, Wi-Fi, storage)
- [ ] `src/agent/Edge/IEdgeProvisioningService.cs` — service interface
- [ ] Program.cs registers EdgeProvisioningService
- [ ] `dotnet build` succeeds for Hercules.csproj
- [ ] Unit tests cover EdgeProvisioningService

## Scope / Likely files
images/raspberry-pi/, deploy/raspberry-pi/

## Dependencies
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)
- блокирует / опирается на: [task_055 — security-ops](task_055.md)

## Risks / Rollback
Hardware-специфичные баги; CI matrix на реальных устройствах.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
