# Task 49 — Human-in-the-loop эскалация

**Phase:** 4
**Initiative:** 24
**Status:** done
**Owner:** —
**Slug:** `human-escalation`

## Goal
Mesh эскалирует неоднозначные, low-confidence, policy-sensitive, разрушительные или budget-exceeding операции с кратким action plan и контекстом для подтверждения человеком. Гейты выполнения обеспечиваются кодом, а не только промптами.

## Acceptance criteria
- [x] `EscalationConfig` added to `MeshConfig`
- [x] `IEscalationService` + `EscalationService` implementation
- [x] `EscalationContext` + `EscalationResult` models with escalation type, severity, action plan
- [x] Escalation triggers in `IntentRouter` (trust admission denial → escalate)
- [x] Escalation triggers in `AgentCore` (low-confidence responses, guardrail failures, verification blocks)
- [x] Escalation priority levels: Low / Medium / High / Critical
- [x] Batch-approve API endpoint (`POST /api/escalations/batch-approve`)
- [x] Escalation status API endpoints (list, get, approve, deny)
- [x] `EscalationPanel.astro` component in hercules-web with batch approve
- [x] DI registration in `MeshServiceExtensions`
- [x] Unit tests for `EscalationService`
- [x] `dotnet build` + `dotnet test` pass

## Sub-tasks
- [x] Add `EscalationConfig` to `AppConfig.cs` (nested in `MeshConfig`)
- [x] Create `EscalationContext.cs` and `EscalationResult.cs` models in `Mesh/Escalation/`
- [x] Create `IEscalationService.cs` and `EscalationService.cs`
- [x] Add `escalations` table to `SqliteSessionStore` schema + persistence methods
- [x] Wire `EscalationService` in `MeshServiceExtensions` DI
- [x] Add `IEscalationService` field + escalation call in `IntentRouter` (trust denial → escalate)
- [x] Add escalation triggers in `AgentCore` (guardrail failures, low-confidence, verification blocks)
- [x] Create `EscalationController.cs` with list/get/approve/deny + batch-approve endpoints
- [x] Wire `EscalationController` in `Hercules.WebApi/Program.cs`
- [x] Create `EscalationPanel.astro` in hercules-web with batch approve UI
- [x] Add `EscalationPanel` to mesh page (`memmesh.astro`)
- [x] Write unit tests for `EscalationService` (6 tests)
- [x] `dotnet build src/agent/Hercules.csproj` — 0 errors
- [x] `dotnet test --filter "FullyQualifiedName~Escalation"` — 6/6 pass

## Validation
```
dotnet build src/agent/Hercules.csproj              → 0 errors (19 pre-existing warnings)
dotnet test --filter "FullyQualifiedName~Escalation" → 6/6 pass
dotnet test tests/Hercules.Agent.Tests/            → 1314/1322 pass
                                                    (8 pre-existing failures:
                                                     OtelServiceTests ×5, NumericValidatorTests ×2,
                                                     BudgetGuardTests ×1)
```

## Implementation notes
- `EscalationConfig` nested in `MeshConfig.Escalation` (default: disabled for backward-compatibility)
- `EscalationService` mirrors `ApprovalService` pattern: in-memory `ConcurrentDictionary` cache + SQLite persistence
- Escalation types: LowConfidence, BudgetExceeded, PolicyDenial, AmbiguousIntent, DestructiveOperation, DelegationTrustLow, HighCostOperation
- Three escalation triggers: (1) guardrail hard-cap pre-check, (2) verification pipeline blocks, (3) low-confidence LLM responses
- Trust admission denials in `IntentRouter` escalate delegation attempts to mesh operators
- Critical escalations log at Warning level (future: pager integration)

## Scope / Likely files
src/agent/Mesh/Escalation/, src/hercules-web/src/components/EscalationPanel.astro

## Dependencies
- блокирует / опирается на: [task_010 — approval-gates](task_010.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Operator fatigue; агрегация эскалаций + приоритезация + удобный batch-approve.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
