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
  - `Skill.Meta.Triggers` renamed to `Skill.Meta.PhraseReceivers` (human-friendly term).
    Backward-compatible read of legacy `triggers:` key in `skill.{id}.meta.json`.
- Updated UI branding and web application headers.

### Added (v2 — Code Execution + Multi-Role Routing + Tools)
- **Stage 1: Multi-role LLM routing**. `AppConfig.Roles` dictionary (`main`, `code_writer`,
  `reflector`). `ILLMClient.CompleteAsync(role, messages, ct)` overload with default
  `role = "main"`. `ResilientLLMClient` routes by role via `RoleRouter`; falls back to main
  if a role is unconfigured. `ReflectionEngine` uses the `reflector` role.
- **Stage 2: Sandboxed code execution**. `CodeExecution/ICodeExecutor` (C# file-based apps,
  `dotnet run --file`). 3 layers of defense: regex pre-scan (`DangerousCodeScanner` with
  25+ default patterns: `File.Delete`, `Process.Start`, `HttpClient`, `Socket`,
  `Assembly.LoadFile`, `DllImport`, `Registry`, `rm -rf`, `bash -c`, `eval`, …) →
  isolated temp dir → POSIX ulimit wrapper + `CancellationTokenSource` timeout.
  Default-deny network, 30 s timeout, 1024 file descriptors, 100 KB code size cap.
  Escape hatch via `SandboxOptions.CustomAllowedNamespaces` (token-based: `"HttpClient"`
  allows `new HttpClient()`).
- **Stage 3: Tool ecosystem**. `Tools/ITool` contract + `ToolRegistry` for LLM prompt
  injection of available tools. Three built-in tools:
  - `http` — `HttpTool`: GET/POST/PUT/DELETE with allow-list domains
    (wildcard `*.example.com`), rate limit (60/min), 10 s timeout, 256 KB max response.
  - `execute_code` — `CodeExecutionTool`: adapter of `ICodeExecutor` for the tool protocol.
  - `a2a` — `A2AClient`: minimal JSON-RPC 2.0 client for agent-to-agent delegation
    (spec: https://a2a-protocol.org/latest/).
  - `mcp` — `McpClient`: stub interface for `ModelContextProtocol` NuGet
    (0.3.0-preview provides only server-side; client SDK ETA Q1-Q2 2026).
- **Stage 4: Tool-aware agent flow**. `AgentCore` recognises JSON actions in LLM output
  (`{"action": "tool", "arguments": {...}}`), executes the tool, feeds the result back
  to the LLM, and produces a final answer. Max 3 tool iterations per turn.
  `mode = "tool"` in `AgentResponse` and `ChatResponseDto`.
- **Sandbox audit table** `sandbox_executions` in SQLite: `id`, `session_id`,
  `code_hash`, `language`, `exit_code`, `status`, `duration_ms`, `blocked_patterns`,
  `created_at`. `ReflectionEngine` reports failure rate over the last 5 executions
  and warns when `> 50 %`.

### Tests
- `scripts/test-phrase-receivers.cs` — 8/8 migration scenarios.
- `scripts/test-multi-role.cs` — 10/10 role config checks.
- `scripts/test-sandbox.cs` — 23/23 scanner + executor end-to-end.
- `scripts/test-tools.cs` — 20/20 registry + HttpTool + A2AClient + CodeExecutionTool.
- `scripts/test-stage4.cs` — 16/16 TryParseAction + sandbox audit + tool injection.

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
