# Task 2 — Жизненный цикл навыков

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `skill-lifecycle`

## Goal
Автоматизировать создание, версионирование, тестирование, оценку, улучшение, депрекацию и откат навыков; high-risk изменения остаются human-gated.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Skills/SkillManager.cs, src/agent/Skills/Versioning/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)

## Risks / Rollback
Само-модификация навыков без approval; нужен явный policy gate.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
