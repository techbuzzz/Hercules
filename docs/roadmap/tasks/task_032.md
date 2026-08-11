# Task 32 — Манифест агента

**Phase:** 3
**Status:** pending
**Owner:** —
**Slug:** `agent-manifest`

## Goal
Каждый агент публикует agent.manifest.json: name, version, capabilities, skills, endpoint, auth, поддерживаемые версии протокола, resource limits, trust metadata.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Manifest/, src/agent/Hercules.WebApi/Controllers/ManifestController.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)

## Risks / Rollback
Расхождение манифеста и реальности; генерация из runtime-state.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
