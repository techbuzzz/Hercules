# Task 53 — Mesh dashboard

**Phase:** 5
**Initiative:** 32
**Status:** pending
**Owner:** —
**Slug:** `mesh-dashboard`

## Goal
Web UI: live agent topology, traffic, health, policy denials, бюджеты, queued tasks, skill usage heatmap, последние eval results.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/hercules-web/src/components/MeshTopology.astro, src/hercules-web/src/pages/mesh.astro

## Dependencies
- блокирует / опирается на: [task_013 — opentelemetry](task_013.md)
- блокирует / опирается на: [task_034 — capability-registry](task_034.md)

## Risks / Rollback
UI-сложность; phase-gated фичи с feature flags.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
