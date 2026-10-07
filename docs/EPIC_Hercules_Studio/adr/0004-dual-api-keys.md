# ADR-0004: Dual API keys (contribute + system)

**Status:** Accepted
**Date:** 2026-08-14

## Context

Current Hercules uses a single `WebApi:ApiKey` for all operations. Studio needs:
- Operator-level access (chat, skills, config read) — everyday work
- Admin-level access (restart, destructive ops, full config replace, MCP add) — rare, privileged
- Read-only monitor access (system key can connect in parallel to checked-out agent)

## Decision

**Two API key roles: contribute + system.**

| Key | Role | Permissions |
|---|---|---|
| **contribute** | operator | chat, skills CRUD, memory, config GET, config PATCH (non-destructive), mesh read, tools enable/disable, marketplace install |
| **system** | admin | all contribute + restart, lifecycle destructive (decommission), config PUT (full replace), MCP add/remove, quota changes, force checkout |

## Implementation

Agent generates both keys at first startup, prints to console, saves to `data/security/keys.json`.

`ApiKeyMiddleware` reads `WebApi:ApiKeys` array:
```json
{
  "WebApi": {
    "ApiKeys": [
      { "key": "hc_contrib_<random>", "role": "contribute" },
      { "key": "hc_sys_<random>", "role": "system" }
    ]
  }
}
```

## Studio behavior

- Both keys stored in `safeStorage` (OS keychain)
- System key used only for system-operations → confirmation dialog before use
- "System mode" indicator in StatusBar
- Runtime elevation: no second connection needed, just send system key for privileged request

## Consequences

- Breaking change: `WebApi:ApiKey` (string) → `WebApi:ApiKeys` (array of {key, role})
- Migration: if `ApiKeys` empty, fallback to legacy `ApiKey` as contribute
- Backend task `task_097` required before Stage 1

## Alternatives considered

- **JWT/OAuth2:** overkill for local agent; API keys simpler
- **Single key + RBAC endpoint:** more complex, no real benefit for single-agent auth
- **mTLS:** too heavy for Studio→local-agent; reserved for mesh peer auth