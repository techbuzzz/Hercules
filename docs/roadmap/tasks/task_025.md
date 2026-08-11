# Task 25 — MCP-адаптер

**Phase:** 2
**Status:** pending
**Owner:** —
**Slug:** `mcp-adapter`

## Goal
Hercules может потреблять выбранные MCP-серверы и экспонировать подходящие инструменты через MCP-совместимый адаптер. Built-in tools остаются .NET-имплементациями без отдельного процесса.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mcp/, src/agent/Tools/McpAdapter/

## Dependencies
- блокирует / опирается на: [task_024 — tool-registry](task_024.md)

## Risks / Rollback
Поверхность атаки MCP-серверов; strict capability allowlist.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
