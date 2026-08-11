# Task 13 — Фундамент OpenTelemetry

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `opentelemetry`

## Goal
ASP.NET, LLM, tool, skill и storage операции эмитят связанные traces, метрики и structured logs. Локальный console/file exporter по умолчанию; OTLP опционально.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/HostBuilderExtensions.cs, src/agent/Observability/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)

## Risks / Rollback
Стоимость трейсинга на долгих задачах; sampling-стратегия обязательна.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
