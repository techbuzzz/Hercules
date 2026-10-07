# ADR-0001: Electron over Tauri

**Status:** Superseded by [ADR-0009 — Web-first Hercules Studio](0009-web-first-studio.md)
**Date:** 2026-08-14

> **Superseded 2026-10-06.** This ADR is kept for history. Its decision was never executed:
> the Electron project never reached a successful `npm install`, and seven of its eight
> views were placeholders. More importantly, its core premise — that Studio needed a native
> shell for filesystem access, process spawning and C# execution — no longer holds, because
> those responsibilities moved into the agent (`DotnetFileBasedExecutor`, the documented
> `supervisor (Studio / systemd / watcher)` restart protocol, A2A discovery,
> `SqliteWorkflowDefinitionStore`). See ADR-0009 for the full analysis.
>
> Two details below are now known to be wrong and should not be reused as arguments:
>
> - *"better-sqlite3 requires a native Node addon build"* — Tauri ships an official SQLite
>   plugin. This is not a real discriminator.
> - *"PWA / web-only: Cannot access FS, tray, notifications, process spawning"* — the
>   agent now provides process supervision and C# execution server-side, and notifications
>   have a Web API. Only an interactive PTY and a tray icon genuinely remain desktop-only.

## Context

Hercules Studio needs a desktop shell that can:
- Run a full IDE-like UI (Monaco, xterm.js, Vue Flow)
- Access local file system and spawn processes
- Bundle complex dependencies (better-sqlite3 native addon)
- Target Windows x64 first
- Support auto-updater

Two main options: Electron and Tauri.

## Decision

**Choose Electron.**

## Rationale

- **JS team experience:** No Rust experience in team; Electron = pure JS/TS
- **Native addons:** better-sqlite3 requires native Node addon build; Electron has mature tooling (electron-rebuild)
- **Monaco integration:** Monaco is designed for Electron/browser; proven in VS Code
- **Windows focus:** Electron + electron-builder has best Windows NSIS/MSI support
- **Auto-updater:** electron-updater + GitHub Releases is mature
- **Ecosystem:** AnythingLLM, Flowise, VS Code, Cursor all use Electron — proven pattern
- **Size (~150MB):** acceptable for IDE product

## Consequences

- Larger bundle (~150MB vs Tauri ~5MB)
- Higher memory (Chromium per window)
- Must follow Electron security best practices (contextIsolation, sandbox, CSP)

## Alternatives considered

- **Tauri 2.0:** Lighter, safer, but Rust IPC barrier + OS webview inconsistencies + native addon complexity for better-sqlite3. Revisit if size becomes critical.
- **PWA / web-only:** Cannot access FS, tray, notifications, process spawning. Not suitable for IDE.