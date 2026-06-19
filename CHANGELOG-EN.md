# Changelog

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- **Rebranding**: the project has been renamed from `MicroHermes` / "Мини-Хермес" to **Hercules**.
  - Renamed namespaces (`MicroHermes.*` → `Hercules.*`), projects
    (`MicroHermes.csproj` → `Hercules.csproj`, `MicroHermes.WebApi` → `Hercules.WebApi`),
    the solution (`Hercules.slnx`), and the frontend directory (`hermes-web` → `hercules-web`).
  - Environment variable prefix: `HERMES_` → `HERCULES_`.
- Updated UI branding and web application headers.

### Added
- Brand assets in `assets/branding/` (logo, monogram, favicon, PNG/ICO exports).
- Full set of repository documentation: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `SECURITY.md`, `CHANGELOG.md`, `.editorconfig`, Issue/PR templates, CI workflow.
- `docs/` directory with detailed guides: quick start, architecture, configuration, API, branding.

## [1.0.0] — 2026-06-18

### Added
- Self-improving agent core: `AgentCore`, `SkillRouter`, `SkillManager`,
  `ReflectionEngine`, `MemoryManager`.
- LLM layer on `Microsoft.Extensions.AI`: YandexGPT (primary), Ollama Cloud / Local,
  LM Studio with automatic fallback (`ResilientLLMClient`).
- Hybrid storage: Markdown/JSON files for skills and memory + SQLite for logs and metrics.
- Interfaces: CLI (REPL on Spectre.Console) and Telegram bot.
- Web API (ASP.NET Core Minimal API) with `X-Api-Key` authentication and CORS.
- Astro + TailwindCSS web interface (chat, skills, profile, stats).
