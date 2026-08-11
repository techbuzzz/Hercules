# Task 11 — Слоистая память

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `layered-memory`

## Goal
Разделить request context, short-lived session/working memory, durable facts и append-only episodic records. Memory writes имеют source, confidence, TTL, sensitivity.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Memory/Layers/, src/agent/Memory/Metadata/

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)

## Risks / Rollback
Утечка чувствительных данных в LLM-контекст; redaction-обязательна.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
