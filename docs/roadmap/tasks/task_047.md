# Task 47 — Retry, timeout, circuit breaker

**Phase:** 4
**Initiative:** 19
**Status:** pending
**Owner:** —
**Slug:** `retry-timeout-breaker`

## Goal
Peer-вызовы защищены deadline-aware retries, exponential backoff с jitter, per-peer circuit breaker'ами и bulkheads. Non-idempotent операции не ретраятся вслепую и требуют idempotency keys.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Resilience/

## Dependencies
- блокирует / опирается на: [task_037 — transports](task_037.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Retry storm; per-peer rate limit + наблюдаемое состояние breaker'ов.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
