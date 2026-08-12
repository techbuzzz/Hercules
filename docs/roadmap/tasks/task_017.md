# Task 17 — Безопасное самоулучшение

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `safe-self-improvement`

## Goal
Maintenance workflow анализирует анонимизированные failures и eval, предлагает versioned skill diff, тесты, ожидаемый gain и rollback plan. Не активирует свои изменения вне approval policy.

## Acceptance criteria

### Sub-tasks

- [x] `Config/AppConfig.cs` — добавить `SelfImprovementConfig` секцию: `Enabled` (default true), `MinSuccessRateThreshold` (default 0.5), `RequireApprovalForProd` (default true), `MaxProposalsPerDay` (default 5), `AnonymizeData` (default true)
- [x] `Reflection/Proposal.cs` — record: `Id`, `SkillId`, `SkillName`, `AnalysisSummary`, `ProposedPrompt`, `ProposedPhrases`, `ExpectedScoreGain`, `RollbackVersion`, `CreatedAt`, `Status` (Proposed/Approved/Applied/Rejected)
- [x] `Reflection/MaintenanceWorkflow.cs` — класс: анализирует failures + eval results через LLM, предлагает versioned skill diff (новый prompt, новые phrases), ожидаемый gain, rollback plan. Сохраняет Proposal.
- [x] `Reflection/ProposalDiffer.cs` — класс: `ComputeDiff(currentSkill, proposedPrompt, proposedPhrases)` → returns structured diff: added/removed/unchanged phrases, prompt delta, next version number
- [x] `Reflection/SelfImprovementService.cs` — DI-friendly сервис: `RunMaintenanceAsync(skillId)` → запускает workflow и возвращает Proposal; `ApplyProposal(id)` → применяет через SkillManager + запускает eval harness; `RejectProposal(id)` → отклоняет. Интеграция с approval gates.
- [x] `Reflection/SelfImprovementController.cs` — WebAPI endpoints: `POST /api/maintenance/run` (запустить maintenance workflow), `GET /api/maintenance/proposals` (список proposals), `GET /api/maintenance/proposals/{id}` (детали), `POST /api/maintenance/proposals/{id}/approve` (применить, с eval harness), `POST /api/maintenance/proposals/{id}/reject` (отклонить)
- [x] `Reflection/ProposalStore.cs` — персистентное хранение proposals в `data/Skills/.proposals/`. Файловый репозиторий: SaveProposal, LoadProposal, ListProposals, GetProposalsBySkill, UpdateStatus.
- [x] `Program.cs` (CLI + WebAPI) — зарегистрировать SelfImprovementConfig singleton и SelfImprovementService в DI
- [x] Unit-тесты: `MaintenanceWorkflowTests.cs` — 9 тестов: analysis generates proposal, no-ops for high success rate, rollback version set correctly, expected gain calculated, daily limit stops at limit
- [x] Unit-тесты: `ProposalDifferTests.cs` — 8 тестов: added/removed phrases, version increment, unchanged diff, prompt delta, estimated gain
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (Reflection tests: 17/17 passed; pre-existing failures in OtelServiceTests and BudgetGuardTests are unrelated)

## Scope / Likely files
src/agent/Reflection/MaintenanceWorkflow.cs, src/agent/Reflection/ProposalDiffer.cs

## Dependencies
- блокирует / опирается на: [task_002 — skill-lifecycle](task_002.md)
- блокирует / опирается на: [task_014 — audit-privacy](task_014.md)
- блокирует / опирается на: [task_016 — eval-harness](task_016.md)

## Validation

```
dotnet build src/agent/Hercules.csproj                     → 0 errors
dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj → 0 errors
dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj → 0 errors
dotnet test --filter "FullyQualifiedName~Reflection"      → 17 passed
dotnet test (full suite)                                  → 573 passed, 6 pre-existing failures (OtelService, BudgetGuard)
```

## Risks / Rollback
Само-модификация без human gate; строгий two-person rule для prod-skills.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
