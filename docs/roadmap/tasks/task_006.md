# Task 6 — Тесты и бенчмарки

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `tests-and-benchmarks`

## Goal
dotnet test покрывает ≥70%; dotnet run --benchmark измеряет skill hit rate, latency, токены, оценочную стоимость, успех инструментов, рост памяти.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
tests/Hercules.Agent.Tests/, src/agent/CLI/Commands/BenchmarkCommand.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_002 — skill-lifecycle](task_002.md)
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)

## Risks / Rollback
70% coverage трудно для LLM-кода; отделить pure-logic от side-effects.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
