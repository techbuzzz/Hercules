# ADR-0005: CheckIn/CheckOut model

**Status:** Accepted
**Date:** 2026-08-14

## Context

Multiple Studio instances may try to connect to the same agent. Need a model to prevent conflicting concurrent operations (like document check-in/check-out).

## Decision

**CheckIn/CheckOut with heartbeat TTL 60s.**

| Endpoint | Key | Description |
|---|---|---|
| `POST /api/system/checkin` | contribute | Studio checks out agent. Body: `{studioId, studioName}`. Returns success or refusal if already checked out |
| `POST /api/system/checkout` | contribute | Release agent |
| `POST /api/system/checkin/heartbeat` | contribute | Heartbeat (60s TTL). Missing → auto checkout |
| `GET /api/system/checkin/status` | any | `{checkedOut, checkedOutBy, checkedOutAt, ttlSeconds}` |
| `POST /api/system/checkin/force` | system | Force checkout (admin override) |

## Rules

- **Contribute key:** one active Studio. Second Studio gets refusal "agent is checked out by {studioName}"
- **System key:** can connect in parallel (read-only monitor mode) — does not check out
- **Heartbeat:** Studio sends heartbeat every 30s (TTL 60s). If Studio crashes → TTL expires → auto checkout
- **Force checkout:** system key can override (with audit log)

## Consequences

- Backend task `task_098` required before Stage 1
- Studio must implement heartbeat timer in ConnectionManager
- On Studio startup: attempt checkin, handle refusal gracefully
- On Studio close: send checkout (best-effort, TTL is backup)

## Alternatives considered

- **No concurrency control:** risky — conflicting skill edits, config changes
- **WebSocket connection = lock:** crash detection works but more complex; heartbeat simpler
- **Per-endpoint locking:** too granular, complex to implement and reason about