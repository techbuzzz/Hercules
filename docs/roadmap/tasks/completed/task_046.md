# Task 46 — Verification pipeline

**Phase:** 4
**Initiative:** 22
**Status:** done
**Owner:** —
**Slug:** `verification-pipeline`

## Goal
High-impact или safety-sensitive ответы проверяются verifier-навыками, числовыми валидаторами, policy-enforcer'ами или независимыми peer-агентами до возврата пользователю или выполнения действия.

## Acceptance criteria
- [x] TBD при старте работы (декомпозиция в sub-tasks)

## Sub-tasks
- [x] `IVerificationPipeline` interface and `VerificationPipeline` implementation
- [x] `VerificationContext` and `VerificationResult` models
- [x] `IVerifier` base interface + `SafetyVerifier` (PII, code injection, dangerous patterns)
- [x] `PolicyVerifier` (trust admission, capability constraints)
- [x] `SchemaVerifier` (response schema validation)
- [x] `NumericValidator` (numeric claim checks)
- [x] `VerificationConfig` added to `MeshConfig`
- [x] Integration into `AgentCore.HandleAsyncCore` (post-response, pre-return)
- [x] DI registration in `MeshServiceExtensions` + `Program.cs`
- [x] Unit tests for all verifiers and pipeline (9 tests)
- [x] `dotnet build` + `dotnet test` pass

## Validation
- `dotnet build src/agent/Hercules.csproj` — succeeded
- `dotnet test --filter "FullyQualifiedName~Verification"` — 9 passed
- `dotnet test tests/Hercules.Agent.Tests/` — pre-existing failures unchanged

## Implementation notes
Triggering logic embedded in each verifier's `CanVerify()` method and `AgentCore` confidence-skip logic.
Blocked responses return a safe degradation message and `VerificationMetadata` in `AgentResponse`.
Default config: disabled (backward-compatible); enable via `Mesh.Verification.Enabled=true` in appsettings.

## Scope / Likely files
src/agent/Mesh/Verification/

## Dependencies
- блокирует / опирается на: [task_007 — typed-contracts](task_007.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Verifier может быть скомпрометирован или ошибочен; meta-verification несколькими независимыми проверками + re-check критических операций.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
