# Task 50 — Distributed reflection

**Phase:** 4
**Initiative:** 20
**Status:** done
**Owner:** —
**Slug:** `distributed-reflection`

## Goal
Отчёты рефлексии включают производительность peer-агентов, routing-решения, паттерны сбоев и предлагают улучшения: новые skills, routing-правила, peer-связи. Формируют proposals, а не unreviewed prod-изменения.

## Acceptance criteria
- [x] `PeerMetric` and `FailurePattern` models
- [x] `ReflectionProposal` and `ReflectionProposalStore` — structured proposal persistence
- [x] `DistributedReflection.CollectPeerMetrics()` — health scores, latency, circuit state
- [x] `DistributedReflection.CollectFailurePatterns()` — failure patterns from audit/CB state
- [x] `DistributedReflection.CollectRoutingPatterns()` — intent → peer resolution patterns
- [x] `DistributedReflection.GenerateProposalsAsync()` — circuit-open → NewSkill proposals, low-health → TrustUpdate proposals
- [x] `DistributedReflection.GenerateLLMProposalsAsync()` — LLM-assisted proposal generation (configurable)
- [x] `DistributedReflection.GetPendingProposals()`, `Approve()`, `Reject()` — human-in-the-loop
- [x] `ReflectionProposalConfig` in `MeshConfig`
- [x] DI registration in `MeshServiceExtensions`
- [x] Unit tests (ProposalStore CRUD, proposal generation, status transitions)
- [x] `dotnet build` + `dotnet test` pass

## Sub-tasks
- [x] `PeerMetric`, `FailurePattern`, `RoutingPattern` models
- [x] `ReflectionProposal`, `ReflectionProposalStore` — structured proposals with JSON persistence
- [x] `ReflectionProposalConfig` in `MeshConfig` (`AppConfig.cs`)
- [x] `DistributedReflection` — inject `RouterHealthTracker`, `MeshAuditService`
- [x] `DistributedReflection.CollectPeerMetrics()` — peer health, latency, circuit state
- [x] `DistributedReflection.CollectFailurePatterns()` — failure patterns from CB + audit
- [x] `DistributedReflection.CollectRoutingPatterns()` — intent → peer resolution patterns
- [x] `DistributedReflection.GenerateProposalsAsync()` — circuit-open → NewSkill, low-health → TrustUpdate
- [x] `DistributedReflection.GenerateLLMProposalsAsync()` — LLM-assisted proposals
- [x] `DistributedReflection.GetPendingProposals()`, `Approve()`, `Reject()` — human-in-the-loop API
- [x] DI registration in `MeshServiceCollectionExtensions`
- [x] Unit tests for `ReflectionProposalStore` (CRUD, status transitions)
- [x] Unit tests for `DistributedReflection` (metric collection, proposal generation)
- [x] `dotnet build src/agent/Hercules.csproj` — 0 errors
- [x] `dotnet test --filter "FullyQualifiedName~DistributedReflection"` — all pass

## Dependencies
- блокирует / опирается на: [task_017 — safe-self-improvement](task_017.md)
- блокирует / опирается на: [task_041 — inter-agent-audit](task_041.md)

## Validation
```
dotnet build src/agent/Hercules.csproj                            → 0 errors
dotnet test --filter "FullyQualifiedName~DistributedReflection"  → 14/14 pass
dotnet test --filter "FullyQualifiedName~ReflectionProposalStore" → 12/12 pass
dotnet test tests/Hercules.Agent.Tests/                           → 1338/1348 pass
                                                                  (10 pre-existing failures in OtelServiceTests,
                                                                   NumericValidatorTests, BudgetGuardTests,
                                                                   WasmToolTests, HerculesBusTests — unrelated)
```

## Implementation notes
Bug fix: `CollectFailurePatterns()` returned empty when `auditService` was null — circuit breaker check was nested inside the audit null-guard. Fixed by moving CB check outside the guard so it always runs regardless of audit availability.

## Risks / Rollback
Перегрузка шумными proposals; rate-limit + ранжирование + человек-в-контуре.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
