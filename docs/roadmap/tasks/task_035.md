# Task 35 — Формат inter-agent сообщений

**Phase:** 3
**Status:** done
**Owner:** —
**Slug:** `delegation-envelope`

## Goal
Версионированный JSON delegation envelope: requestId, traceId, idempotencyKey, sender, recipient, intent, typed payload, replyTo, deadline, auth context, requested response schema.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/Schema/` — создать папку для типов схем
- [x] `src/agent/Mesh/Schema/ResponseSchema.cs` — `ResponseSchema` record: schema type, example schema, content type
- [x] `src/agent/Mesh/Schema/AuthContext.cs` — `AuthContext` record: token, claims, delegation depth
- [x] Обновить `IntentEnvelope`:
  - [x] добавить `IdempotencyKey: string?`
  - [x] добавить `Recipient: string?`
  - [x] добавить `Auth: AuthContext?`
  - [x] добавить `ResponseSchema: ResponseSchema?`
  - [x] добавить `Version: string` = "1.0"
- [x] Обновить `IntentTransport.SendToAsync` — передавать auth context в headers
- [x] Unit tests: `IntentEnvelopeTests.cs` — serialization roundtrip, null handling
- [x] `dotnet build` — 0 errors (warnings pre-existing)
- [x] `dotnet test` — Phase tests pass (223/223)

## Scope / Likely files
src/agent/Mesh/Envelope/, src/agent/Mesh/Schema/

## Dependencies
- блокирует / опирается на: [task_007 — typed-contracts](task_007.md)

## Risks / Rollback
Эволюция схемы; явные semver + миграции.

## Implementation notes

### 2026-08-13

**Добавлено:**

**`src/agent/Mesh/Schema/`** — новая папка для типов схем:
- `ResponseSchema.cs` — тип для запрошенной схемы ответа: schema_type (json/text/xml/binary), json_schema, content_type, version
- `AuthContext.cs` — auth context для delegation: auth_type, token, claims, delegation_depth, root_request_id

**`src/agent/Mesh/IntentEnvelope.cs`** — расширенный delegation envelope:
- Добавлены новые поля: `IdempotencyKey`, `Recipient`, `Auth` (AuthContext), `ResponseSchema`, `Version`, `Deadline`
- Изменено: `TimeoutMs` → `Deadline` (DateTimeOffset вместо int)
- Добавлен factory method `IntentEnvelope.Create<T>()` для typed payload
- Добавлен backward-compatible constructor (obsolete) для existing code
- Добавлены методы `IsExpired`, `GetDeadlineOrDefault()`
- Добавлен `IntentResponse.SchemaMismatch()` factory method

**`src/agent/Mesh/IntentTransport.cs`** — обновлен SendToAsync:
- Forward auth context в HTTP headers (Authorization, X-Delegation-Depth, X-Root-Request-Id)
- Forward idempotency key в X-Idempotency-Key header
- Timeout вычисляется из Deadline

**`tests/Phase3Tests/IntentEnvelopeTests.cs`** — добавлены тесты:
- Envelope_With_AllNewFields_SerializesCorrectly
- Envelope_Create_WithTypedPayload_SerializesCorrectly
- Envelope_Deadline_Expires_Correctly
- Envelope_GetDeadlineOrDefault_ReturnsDeadline
- IntentResponse_SchemaMismatch_Has_SchemaMismatch_Status

**Validation:**
- `dotnet build` — 0 errors, warnings pre-existing (OpenTelemetry, async test patterns)
- `dotnet test Phase` — 223/223 passed

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
