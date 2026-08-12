# Task 65 — Mesh observability

**Phase:** 4
**Initiative:** 25
**Status:** pending
**Owner:** —
**Slug:** `mesh-observability`

## Goal
Каждый локальный и межагентный шаг эмитит коррелированные traces, метрики и структурированные логи. Mesh-трафик, routing-решения, ретраи и взаимодействия с хранилищами видимы и атрибутируемы per request и per agent.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Observability/, src/agent/Observability/MeshEnrichment.cs

## Dependencies
- блокирует / опирается на: [task_013 — opentelemetry](task_013.md)
- блокирует / опирается на: [task_041 — inter-agent-audit](task_041.md)
- блокирует / опирается на: [task_043 — mesh-router](task_043.md)

## Risks / Rollback
PII и секреты в трейсах; централизованная redaction + sampling policy.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
