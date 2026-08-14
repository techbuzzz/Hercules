# Task 98 — CheckIn/CheckOut protocol

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `checkin-checkout-protocol`
**Studio Stage:** 1

## Goal
Реализовать CheckIn/CheckOut протокол: пока одна Studio подключена (contribute key), агент "Checked Out", второе подключение невозможно. System key может подключаться параллельно (read-only monitor). См. [ADR-0005](../EPIC_Hercules_Studio/adr/0005-checkin-checkout.md).

## Acceptance criteria
- [ ] `CheckInService` — in-memory + optional SQLite persistence
  - `CheckIn(studioId, studioName, role)` → success or refusal (if already checked out by other contribute key)
  - `Heartbeat(studioId)` → update lastSeen (TTL 60s)
  - `CheckOut(studioId)` → release
  - `GetStatus()` → `{checkedOut, checkedOutBy, checkedOutAt, ttlSeconds, role}`
  - `ForceCheckOut()` → system key only, audit logged
  - Background timer: cleanup expired checkins (TTL 60s)
- [ ] `SystemController` — endpoints:
  - `POST /api/system/checkin` (contribute) → checkin
  - `POST /api/system/checkout` (contribute) → checkout
  - `POST /api/system/checkin/heartbeat` (contribute) → heartbeat
  - `GET /api/system/checkin/status` (any auth) → status
  - `POST /api/system/checkin/force` (system) → force checkout
- [ ] Contribute key = one active checkin. Second contribute checkin → 409 Conflict "agent is checked out by {studioName}"
- [ ] System key checkin → allowed in parallel (read-only monitor, does not check out)
- [ ] Auto checkout on TTL expiry (60s without heartbeat)
- [ ] Audit log: checkin, checkout, force, TTL expiry
- [ ] Unit tests: checkin success/refusal, heartbeat, TTL expiry, force, system parallel
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- task_097 (dual API keys — нужны роли)

## Scope / Likely files
src/agent/Hercules.WebApi/Controllers/SystemController.cs (new), src/agent/System/CheckInService.cs (new), src/agent/Hercules.WebApi/Program.cs

## Links
- ADR-0005: [../EPIC_Hercules_Studio/adr/0005-checkin-checkout.md](../EPIC_Hercules_Studio/adr/0005-checkin-checkout.md)
- Backlog: [../backlog.md](../backlog.md)