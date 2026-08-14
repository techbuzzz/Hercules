# Task 47 — Retry, timeout, circuit breaker

**Phase:** 4
**Initiative:** 19
**Status:** done
**Owner:** —
**Slug:** `retry-timeout-breaker`

## Goal
Peer-вызовы защищены deadline-aware retries, exponential backoff с jitter, per-peer circuit breaker'ами и bulkheads. Non-idempotent операции не ретраятся вслепую и требуют idempotency keys.

## Acceptance criteria
- [x] `ResilienceConfig` section in `MeshConfig`
- [x] Jitter in `RetryPolicy.GetDelay()` + `MaxDelay` enforcement
- [x] `ResilientTransport` wrapper: CB check → retry loop with jitter → bulkhead
- [x] `CircuitBreaker` configured from `ResilienceConfig.FailureThreshold`
- [x] `RetryPolicy` configured from `ResilienceConfig`
- [x] DI registration in `MeshServiceExtensions`
- [x] `CircuitBreaker` integrated into `FanOutOrchestrator` (peer filtering)
- [x] `appsettings.json` updated with `Resilience` section
- [x] Unit tests for `CircuitBreaker`, `RetryPolicy`, `ResilientTransport`
- [x] `dotnet build` + `dotnet test` pass

## Sub-tasks
- [x] Add `ResilienceConfig` to `MeshConfig` in `AppConfig.cs`
- [x] Add jitter to `RetryPolicy.GetDelay()` (deterministic + random)
- [x] Configure `CircuitBreaker` from `ResilienceConfig` in DI
- [x] Configure `RetryPolicy` from `ResilienceConfig` in DI
- [x] Create `ResilientTransport` wrapper (`src/agent/Mesh/Resilience/ResilientTransport.cs`)
- [x] Implement bulkhead pattern via per-peer semaphore in `ResilientTransport`
- [x] Integrate `CircuitBreaker` into `FanOutOrchestrator` peer filtering
- [x] Add `Resilience` section to `appsettings.json`
- [x] Write unit tests in `tests/Hercules.Agent.Tests/Mesh/Resilience/`
- [x] `dotnet build src/agent/Hercules.csproj` — succeed
- [x] `dotnet test --filter "FullyQualifiedName~Resilience"` — pass

## Scope / Likely files
src/agent/Mesh/Resilience/

## Dependencies
- блокирует / опирается на: [task_037 — transports](task_037.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Retry storm; per-peer rate limit + наблюдаемое состояние breaker'ов.

## Validation
```
dotnet build src/agent/Hercules.csproj                          → 0 errors
dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj → 0 errors
dotnet test --filter "FullyQualifiedName~Resilience" --no-build  → 18/18 pass
dotnet test tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj → 1307/1316 pass
                                                              (9 pre-existing failures:
                                                               OtelServiceTests ×5, NumericValidatorTests ×2,
                                                               BudgetGuardTests ×1, WasmToolTests ×1)
```

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
