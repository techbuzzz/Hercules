# Task 8 — Ограниченный цикл исполнения

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `bounded-execution`

## Goal
Plan-act-observe с настраиваемыми max steps, wall-clock timeout, cancellation, recursion depth и per-request лимитом вызовов инструментов. Прямой ответ навыка остаётся fast-path.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/AgentCore.cs (limits), src/agent/Loop/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_007 — typed-contracts](task_007.md)

## Risks / Rollback
Слишком жёсткие лимиты ломают сложные задачи; нужны настраиваемые per-skill бюджеты.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
