# Changelog

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

Nothing queued. This is the first tag cut from this repository — the `1.0.0` section
below was documented but never tagged, so there is no earlier `vX.Y.Z` to compare against.

## [2.0.0] - 2026-10-07

The web-first release. The Electron shell is gone and the agent serves the Studio SPA
itself. This is a major bump from the documented `1.0.0` because the change removes a
runtime surface that existing installations depend on.

### Breaking
- **Electron removed; web-first (ADR-0009).** The agent now serves the Studio SPA itself at
  `/ui`. There is no separate frontend process and no Electron shell.
- **`src/hercules-web` (Astro) deprecated, not ported.** Its panels overlapped endpoints
  Studio already uses. Replacement map: `src/hercules-web/DEPRECATED.md`.
- **UI dev port is 4330, not 4321.** The agent's dev CORS whitelist is `4322/4330/8421`.
- **Default agent port 5000 → 8421** (`task_096`, ADR-0003).
  - `Hercules.WebApi` now binds to `http://localhost:8421` (Development) /
    `http://0.0.0.0:8421` (Production) instead of port 5000.
  - `Mesh.Endpoint` default in `appsettings.json` and `AppConfig.MeshConfig.Endpoint`
    changed to `http://localhost:8421`.
  - CORS dev-fallback origins updated: `http://localhost:5000` →
    `http://localhost:8421` (Studio continues to scan 5000 as a legacy
    fallback; see ADR-0003).
  - **Migration path:** existing installations must set
    `ASPNETCORE_URLS=http://localhost:8421` (or `--urls …`) or update
    `Mesh.Endpoint` in `appsettings.json` / `runtime-config.json`. To keep
    the old port, set `ASPNETCORE_URLS=http://0.0.0.0:5000` explicitly.
  - Reason: 5000 collides with Flask, Synology DSM, UPnP, Syncthing. See
    [ADR-0003](docs/EPIC_Hercules_Studio/adr/0003-port-range-8421.md).
- **Rebranding**: the project has been renamed from `MicroHermes` / "Мини-Хермес" to **Hercules**.
  - Renamed namespaces (`MicroHermes.*` → `Hercules.*`), projects
    (`MicroHermes.csproj` → `Hercules.csproj`, `MicroHermes.WebApi` → `Hercules.WebApi`),
    the solution (`Hercules.slnx`), and the frontend directory (`hermes-web` → `hercules-web`).
  - Environment variable prefix: `HERMES_` → `HERCULES_`.
  - `Skill.Meta.Triggers` renamed to `Skill.Meta.PhraseReceivers` (human-friendly term).
    Backward-compatible read of legacy `triggers:` key in `skill.{id}.meta.json`.

### Added

#### Studio (web-first)
- **Studio SPA** (`src/hercules-studio`, Vue 3 + Vite): Agents, Chat, Skills, Mesh, Tools,
  Config, Workflow, Decisions, Consensus, Context, LLM.
- **Skills:** Monaco editor, prompt history with an LCS **revision diff**, restore, create
  from 5 templates, cross-agent push with per-agent results, and a `.skillpkg` builder
  written against `docs/skill-package-spec.md`.
- **Mesh explorer:** Vue Flow topology canvas (colour = health, size = capability count),
  node details panel, 6-action context menu, router explorer with phrase search and
  sort/filters, shared-memory browser, circuit-breaker panel, traffic/skill-heatmap/eval
  dashboard panels, and 30s auto-refresh that skips while the tab is hidden.
- **Consensus:** parallel fan-out to several agents with partial-failure tolerance, manual
  pick or a structured LLM judge (`{best_index, rationale}`) with the winner highlighted,
  background notifications, and persisted round history.
- **MCP management:** add/edit/delete/reload over config merge-patch, plus a per-server
  enabled toggle.
- **Config:** LLM provider editor, quotas editor, context budget/distillation, session-store
  backend switch (staged, restart-gated).
- **Roles editor** (`/api/auth/keys`, system role only). Keys are addressed by a
  non-reversible fingerprint; the plaintext is never readable, and a generated key is
  returned exactly once.
- **Strict TypeScript:** `strict` plus `noUncheckedIndexedAccess`.

#### Agent backend
- **Context distillation** (`task_102`): hierarchical context compression
  (`recent` raw + `older` summaries + `ancient` key-facts) to save tokens in
  long sessions.
  - `ContextConfig.Distillation` block: `Mode` (Off/Auto/Manual), `Strategy`
    (hierarchical), `RecentRawCount`, `SummaryInterval`, `KeyFactsExtraction`,
    `MaxAncientFacts`, `SummaryTokenBudget`, `KeyFactsTokenBudget`, plus
    `DistillationPreset` (Economy/Balanced/Full).
  - `IDistillationStore` + `SqliteDistillationStore` for persistence
    (`context_summaries`, `context_key_facts` tables, upsert by
    `(session_id, fact_text)`).
  - `ContextDistillationService` — deterministic summarization (no LLM in the
    hot path: word-frequency topics + sentence scoring; n-gram frequency +
    dedup for key-facts). `DistillAsync` and `GetSummaryAsync`.
  - `ContextBuilder` integrates the distilled block additively when
    `Distillation.Mode != Off` (legacy facts+episodes+working path preserved).
  - New endpoints: `POST /api/context/distill` (run on demand or per-session),
    `GET /api/context/summary?sessionId=…&maxTokens=…` (returns real markdown
    block instead of placeholder). `WithName` set for Orval codegen.
  - `SqliteSessionStore.GetSessionInteractionsAsync(sessionId, limit, ct)` —
    chronological list of `InteractionLog` for the service to consume.
  - See [task_102.md](docs/roadmap/tasks/task_102.md) for implementation notes.
- **Mesh observability** (`task_065`): `IMeshObservabilityService` wired into
  `CapabilityMeshRouter`, `ResilientTransport`, `IntentRouter`, `TaskLifecycleProtocol`
  and `FanOutOrchestrator`. New `GET /api/mesh/observability/status` and
  `/config` endpoints, plus `RecordMeshMetric("retry_attempt", …)`.
- **Multi-role LLM routing.** `AppConfig.Roles` dictionary (`main`, `code_writer`,
  `reflector`). `ILLMClient.CompleteAsync(role, messages, ct)` overload with default
  `role = "main"`. `ResilientLLMClient` routes by role via `RoleRouter`; falls back to
  main if a role is unconfigured. `ReflectionEngine` uses the `reflector` role.
- **Sandboxed code execution.** `CodeExecution/ICodeExecutor` (C# file-based apps,
  `dotnet run --file`). 3 layers of defense: regex pre-scan (`DangerousCodeScanner`
  with 25+ default patterns: `File.Delete`, `Process.Start`, `HttpClient`, `Socket`,
  `Assembly.LoadFile`, `DllImport`, `Registry`, `rm -rf`, `bash -c`, `eval`, …) →
  isolated temp dir → POSIX ulimit wrapper + `CancellationTokenSource` timeout.
  Default-deny network, 30 s timeout, 1024 file descriptors, 100 KB code size cap.
  Escape hatch via `SandboxOptions.CustomAllowedNamespaces` (token-based:
  `"HttpClient"` allows `new HttpClient()`).
- **Tool ecosystem.** `Tools/ITool` contract + `ToolRegistry` for LLM prompt injection:
  `http` (`HttpTool`, allow-list domains, 60/min, 10 s timeout, 256 KB cap),
  `execute_code` (`CodeExecutionTool`), `a2a` (`A2AClient`, JSON-RPC 2.0 per
  https://a2a-protocol.org/latest/), `mcp` (`McpClient`).
- **Tool-aware agent flow.** `AgentCore` recognises JSON actions in LLM output
  (`{"action": "tool", "arguments": {...}}`), executes the tool, feeds the result back to
  the LLM and produces a final answer. Max 3 tool iterations per turn; `mode = "tool"` in
  `AgentResponse` and `ChatResponseDto`.
- **Sandbox audit table** `sandbox_executions` in SQLite. `ReflectionEngine` reports
  failure rate over the last 5 executions and warns when `> 50 %`.
- **`Hercules.SkillSdk` NuGet package** (`task_101`): whitelist interfaces for file-based
  C# skills (`IHttpClient`, `IMcpClient`, `ILlmClient`, `IMemoryClient`, `ISkillLogger`,
  `ISessionContext`, aggregate `IHerculesSkillContext`) with agent-side adapters enforcing
  allowed domains, MCP tool allow-list, memory scopes and session isolation. Plus
  `SkillSdkExecutor`, which compiles these skills in a collectible `AssemblyLoadContext`
  via Roslyn.
- **Security Operations** (`task_055`): fleet-wide identity rotation, credential
  revocation, certificate renewal, package signing verification, vulnerability reporting,
  security audit export (`IFleetIdentityService`, `ICertificateService`,
  `IPackageSigningService`, `IVulnerabilityReporter`, `ISecurityAuditExporter`).

#### Infrastructure
- **CI** (`.github/workflows/ci.yml`): agent build/test, Studio typecheck/lint/unit/E2E, and
  an OpenAPI freshness job that fails on any drift.
- Brand assets in `assets/branding/` (logo, monogram, favicon, PNG/ICO exports).
- Full set of repository documentation: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `SECURITY.md`, `CHANGELOG.md`, `.editorconfig`, Issue/PR templates.

### Changed
- Updated UI branding and web application headers.

### Security
- **`GET /api/config` no longer returns credentials.** It previously served live LLM API
  keys, the Postgres connection string (with password), a Telegram bot token and a signing
  key to *any* authenticated session, `contribute` role included. Secrets are now redacted
  by name, and `PATCH /api/config` strips the redaction marker before merging so masked
  values cannot overwrite the real secret on round-trip.

### Fixed
- `McpClientService` ignored `Enabled`, so disabled servers were still dialled and their
  tools registered.
- Mesh agent list rendered permanently empty: the endpoint returns `{count, agents}` and the
  view checked `Array.isArray`.
- Studio auth: keys are snapshotted per request rather than at startup, so a role change
  applies immediately; deleting or demoting a key revokes its live sessions.
- Workflow editing created duplicates — the save request had no `Id`, so every edit minted a
  new definition.
- 314 broken relative links across ~104 roadmap task files.

### Tests
- 2276 agent tests passing (`tests/Hercules.Agent.Tests`).
- 97 Studio unit tests, 30 Studio E2E tests (`Playwright`).
- Standalone scenario scripts retained under `scripts/`: `test-phrase-receivers.cs`,
  `test-multi-role.cs`, `test-sandbox.cs`, `test-tools.cs`, `test-stage4.cs`.
- Docs guards re-runnable via `scripts/`: `check-doc-links.cjs`,
  `check-backlog-status.cjs`, `check-ci-workflow.cjs`, `count-stage-tasks.cjs`.

## [1.0.0] - 2026-06-18

> Never tagged — recorded here as the previously documented state.

### Added
- Self-improving agent core: `AgentCore`, `SkillRouter`, `SkillManager`,
  `ReflectionEngine`, `MemoryManager`.
- LLM layer on `Microsoft.Extensions.AI`: YandexGPT (primary), Ollama Cloud / Local,
  LM Studio with automatic fallback (`ResilientLLMClient`).
- Hybrid storage: Markdown/JSON files for skills and memory + SQLite for logs and metrics.
- Interfaces: CLI (REPL on Spectre.Console) and Telegram bot.
- Web API (ASP.NET Core Minimal API) with `X-Api-Key` authentication and CORS.
- Astro + TailwindCSS web interface (chat, skills, profile, stats).