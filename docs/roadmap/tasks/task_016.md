# Task 16 — Оценка навыков (eval harness)

**Phase:** 1
**Status:** pending
**Owner:** —
**Slug:** `eval-harness`

## Goal
Каждый навык имеет deterministic fixtures и опциональные LLM-judge кейсы. Baseline записывается до promotion, регрессии блокируют автоматический rollout.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Skills/Eval/, src/agent/CLI/Commands/SkillEvalCommand.cs

## Dependencies
- блокирует / опирается на: [task_002 — skill-lifecycle](task_002.md)
- блокирует / опирается на: [task_007 — typed-contracts](task_007.md)

## Risks / Rollback
Flaky LLM-judge; нужны reproducibility-инварианты (temperature=0, seed).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
