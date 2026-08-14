# Task 102 — Context distillation

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `context-distillation`
**Studio Stage:** 6

## Goal
Иерархическая дистилляция контекста (recent=raw, older=summary, ancient=key facts) для экономии токенов. Расширить существующий `/api/context/*` endpoints.

## Acceptance criteria
- [ ] `ContextDistillationService` — иерархическая компрессия:
  - Recent messages (last N) — raw, unchanged
  - Older messages — LLM summarization: periodic summary every M messages
  - Ancient messages — key facts extraction: store as compact facts
  - Config: `ContextConfig.Distillation` = { mode: off|auto|manual, strategy: hierarchical, summaryInterval, keyFactsExtraction }
- [ ] Endpoints:
  - `POST /api/context/distill` → run distillation on current session context
  - `GET /api/context/summary` → current summary (markdown)
  - `POST /api/context/trace/compress` → compress trace (existing, enhanced)
  - `GET /api/context/budget` → current context budget status (existing)
- [ ] `AgentCore` — перед LLM call: assemble context with distillation (raw recent + summary older + key facts ancient)
- [ ] Context budget: maxTokens, maxMessages, maxToolOutputBytes (configurable)
  - Presets: economy (low), balanced (medium), full (high)
- [ ] Persistence: summaries stored in SQLite/session store
- [ ] Unit tests: distillation logic, budget enforcement, key facts extraction
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- task_027 (context assembly) — done

## Scope / Likely files
src/agent/Context/ContextDistillationService.cs (new), src/agent/Context/ContextService.cs (extend), src/agent/Hercules.WebApi/Controllers/ContextController.cs (extend)

## Links
- Studio Stage 6: [../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md](../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md)
- Backlog: [../backlog.md](../backlog.md)