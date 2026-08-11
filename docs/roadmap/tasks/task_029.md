# Task 29 — Score качества навыка

**Phase:** 2
**Status:** pending
**Owner:** —
**Slug:** `skill-quality-score`

## Goal
Per-version метрики: acceptance rate, test score, user correction rate, fallback rate, latency, cost, safety denials. Promotion и routing используют score без монополии.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Skills/Quality/SkillQualityScore.cs

## Dependencies
- блокирует / опирается на: [task_014 — audit-privacy](task_014.md)
- блокирует / опирается на: [task_016 — eval-harness](task_016.md)

## Risks / Rollback
Goodhart law; score — не единственный критерий promotion.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
