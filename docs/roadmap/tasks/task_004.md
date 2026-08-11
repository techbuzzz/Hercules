# Task 4 — Мульти-провайдер LLM

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `multi-provider-llm`

## Goal
YandexGPT, Ollama Cloud/Local, LM Studio и OpenAI-совместимые провайдеры через Microsoft.Extensions.AI с health-check, retry, fallback, capability detection.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/LLM/, src/agent/LLM/Providers/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)

## Risks / Rollback
Разные capabilities провайдеров ломают единые типы; нужны capability-флаги.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
