# Task 59 — Edge provisioning

**Phase:** 5
**Status:** pending
**Owner:** —
**Slug:** `edge-provisioning`

## Goal
SD-card и Docker images для Raspberry Pi: first-boot Wi-Fi, identity enrolment, API key/cert activation, локальная инициализация storage, secure defaults.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

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
