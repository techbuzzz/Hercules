# Task 42 — Контрактные и chaos тесты

**Phase:** 3
**Status:** done
**Owner:** —
**Slug:** `protocol-tests`

## Goal
Protocol fixtures проверяют обратную совместимость и обработку malformed messages; локальные test-агенты симулируют таймауты, дубли, недоступность и schema mismatch.

## Acceptance criteria

### Sub-tasks

- [x] `tests/Phase3Tests/ProtocolCompatibilityTests.cs` — backward compatibility tests: all known envelope versions (v1, v2), message schema evolution, unknown field handling
- [x] `tests/Phase3Tests/MalformedMessageTests.cs` — malformed message handling: missing required fields, invalid types, oversized payloads, garbled JSON, null values
- [x] `tests/Phase3Tests/TransportFaultTests.cs` — transport fault simulation: timeout injection, connection reset, partial response, unreachable peer
- [x] `tests/Phase3Tests/DuplicateMessageTests.cs` — duplicate handling: idempotency key tests, re-send scenarios, deduplication window
- [x] `tests/Phase3Tests/TestAgents/` — local test agents: `MockAgent`, `SlowAgent`, `FailingAgent`, `SchemaMismatchAgent` implementing IAgent interface
- [x] `tests/Phase3Tests/TestAgents/TestTransport.cs` — `TestTransport` stub: controllable latency, failure injection, response queuing
- [x] `tests/Phase3Tests/SchemaMismatchTests.cs` — schema mismatch handling: unknown fields ignored, missing optional fields, type coercion edge cases
- [x] `dotnet build` — 0 errors
- [x] `dotnet test Phase3` — all Phase 3 tests pass (297/297)

## Implementation notes

### 2026-08-13

**`tests/Phase3Tests/ProtocolCompatibilityTests.cs`** — 9 tests covering:
- v1 envelope deserialization (missing new fields)
- Unknown/future fields ignored
- Minimal fields
- Full current version round-trip
- Old timestamp format
- Missing RequestId graceful handling
- Response v1 deserialization
- Full round-trip with all fields
- CamelCase serialization round-trip

**`tests/Phase3Tests/MalformedMessageTests.cs`** — 20 tests covering:
- Garbled/invalid JSON (throws JsonException)
- Empty/whitespace JSON (throws)
- Array instead of object (throws)
- Missing required fields
- Wrong types for fields
- Oversized payloads (1MB)
- Null values
- Invalid UTF-8
- Deeply nested
- Control characters preserved
- Response malformed JSON (throws)
- Duplicate keys (last wins)
- Unicode preservation
- Special character escaping

**`tests/Phase3Tests/TransportFaultTests.cs`** — 11 tests covering:
- Timeout simulation via TestTransport
- Unreachable peer simulation
- Transport error injection
- Policy rejection simulation
- Successful path
- Unknown agent
- Latency reporting
- Recovery after failure cleared
- Disposed transport throws
- ITransport interface compliance
- Cancellation token propagation

**`tests/Phase3Tests/DuplicateMessageTests.cs`** — 12 tests covering:
- IdempotentAgent: first request succeeds
- IdempotentAgent: duplicate request rejected
- IdempotentAgent: different keys both succeed
- IdempotentAgent: no idempotency key uses RequestId
- IdempotentAgent: reset clears processed keys
- IdempotentAgent: multiple unique keys
- IdempotentAgent: tracks all received requests
- IntentEnvelope idempotency key round-trip
- Null idempotency key round-trip
- Unicode idempotency key
- MockAgent receives envelope with key
- Empty string idempotency key (uses RequestId)

**`tests/Phase3Tests/TestAgents/TestAgentStubs.cs`** — 5 test agents:
- `SlowAgent`: configurable delay
- `FailingAgent`: configurable error or exception
- `SchemaMismatchAgent`: returns wrong schema type
- `MockAgent`: deterministic responses, tracks calls
- `IdempotentAgent`: deduplication by idempotency key

**`tests/Phase3Tests/TestAgents/TestTransport.cs`** — `TestTransport`:
- Implements `ITransport`
- Configurable latency (Task.Delay)
- Configurable failure injection (all TransportErrorKind variants)
- Cancellation support
- Returns TransportResult with correct latency

**`tests/Phase3Tests/SchemaMismatchTests.cs`** — 13 tests covering:
- SchemaMismatchAgent returns plain text when JSON expected
- IntentResponse.SchemaMismatch factory
- ResponseSchema round-trip (JSON + Text types)
- Default schema values
- No schema round-trip
- ContentType preservation
- Extra fields in response
- Unknown schema type deserialization
- Null schema deserialization
- TransportResult schema mismatch kind

**Validation:**
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj -c Release` — 0 errors (57 pre-existing warnings)
- `dotnet test Phase3` — 297/297 passed
- `dotnet test Phase4` — 29/29 passed

## Scope / Likely files
tests/Hercules.Agent.Tests/Mesh/

## Dependencies
- блокирует / опирается на: [task_035 — delegation-envelope](task_035.md)
- блокирует / опирается на: [task_036 — task-lifecycle-protocol](task_036.md)
- блокирует / опирается на: [task_037 — transports](task_037.md)

## Risks / Rollback
Сложно воспроизводимо; нужен deterministic test harness.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
