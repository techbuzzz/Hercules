# Task 47 — Retry, timeout, circuit breaker

**Phase:** 4
**Status:** pending
**Owner:** —
**Slug:** `retry-timeout-breaker`

## Goal
Неудачные peer-вызовы используют deadline-aware retries, exponential backoff с jitter, bulkheads, rate limits и circuit breakers. Non-idempotent вызовы не ретраятся вслепую.

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
src/agent/Mesh/Resilience/

## Dependencies
- блокирует / опирается на: [task_037 — transports](task_037.md)
- блокирует / опирается на: [task_040 — trust-admission](task_040.md)

## Risks / Rollback
Retry storm; per-peer rate limit + breaker state observability.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
