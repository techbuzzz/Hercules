# ADR-0001: Electron over Tauri

**Status:** Accepted
**Date:** 2026-08-14

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