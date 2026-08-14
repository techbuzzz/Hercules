# ADR-0006: SQLite (better-sqlite3) for local Studio storage

**Status:** Accepted
**Date:** 2026-08-14

## Context

Studio needs local persistent storage for:
- Chat history per connection
- Skill drafts (local before push to agent)
- Workflow definitions (MVP, until workflow-server)
- Scan cache

Options: JSON files, SQLite, IndexedDB (renderer), LevelDB.

## Decision

**Use better-sqlite3 (synchronous SQLite for Node.js) in main process.**

## Rationale

- **Structured queries:** chat history needs filtering by connection/date; SQL is natural
- **Main process:** better-sqlite3 is a native Node addon, runs in main process, accessed via IPC
- **Synchronous API:** better-sqlite3 is sync = simpler code, no callback complexity, fast for local DB
- **Single file:** `userData/studio.db` — easy backup, portable
- **Migrations:** simple versioned schema in code

## Schema

See [SYSTEM-DESIGN.md §8.1](../SYSTEM-DESIGN.md#81-sqlite-userdatstudiodb)

Tables: `chat_history`, `skill_drafts`, `workflows`, `scan_cache`.

## What is NOT in SQLite

- Connection registry → `userData/connections.json` (simple, human-readable)
- Settings → `userData/settings.json`
- API keys → `safeStorage` (OS keychain)
- License consent → `userData/license-consent.json`

## Consequences

- Native addon rebuild for Electron (`electron-rebuild`)
- Main process only — renderer accesses via IPC
- No async driver needed (better-sqlite3 is sync, fast for local)

## Alternatives considered

- **JSON files:** simple but no structured queries, slow for large chat history
- **IndexedDB:** renderer-only, hard to backup, no main process access
- **LevelDB:** key-value only, no SQL queries
- **lowdb:** JSON with lodash — too simple for structured data