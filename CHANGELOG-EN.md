# Changelog

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added (Phase 8 — Hercules Studio backend prerequisites)
- **Context distillation (task_102)**: hierarchical context compression
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
  - 19 new unit tests (`SqliteDistillationStoreTests` × 8,
    `ContextDistillationServiceTests` × 11). Full Context suite: 103/103.
  - See [task_102.md](docs/roadmap/tasks/task_102.md) for implementation notes.

### Changed
- **BREAKING: Default agent port 5000 → 8421** (`task_096`, ADR-0003).
  - `Hercules.WebApi` now binds to `http://localhost:8421` (Development) /
    `http://0.0.0.0:8421` (Production) instead of port 5000.
  - `Mesh.Endpoint` default in `appsettings.json` and `AppConfig.MeshConfig.Endpoint`
    changed to `http://localhost:8421`.
  - CORS dev-fallback origins updated: `http://localhost:5000` →
    `http://localhost:8421` (Studio continues to scan 5000 as a legacy
    fallback; see ADR-0003).
  - `hercules-web` defaults (`API_BASE`, `PUBLIC_API_BASE`, embedded
    `MeshRouterPanel`/`AgentCardPanel`) follow the new port.
  - All documentation, smoke tests and curl examples updated.
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
- Updated UI branding and web application headers.

### Added (Phase 4 — Mesh Observability)

- **Mesh observability wiring** (`task_065`): `IMeshObservabilityService` is now wired into:
  - `CapabilityMeshRouter` — routing decision spans and `routing_decision` metrics per RouteAsync call
  - `ResilientTransport` — retry attempt spans (`ResilientTransport.Retry.{N}`), circuit-breaker rejection events, and `retry_attempt` metrics
  - `IntentRouter` — delegation spans (`IntentRouter.Delegate`) with trace context enrichment and `delegation` / `delegation_latency_ms` metrics
  - `TaskLifecycleProtocol` — optional observability service injection (no-op when not set)
  - `FanOutOrchestrator` — fan-out span (`FanOut.Orchestrate`) with `fanout.no_peers` events
- `IMeshObservabilityService.RecordMeshMetric("retry_attempt", ...)` support added
- `GET /api/mesh/observability/status` endpoint returns mesh observability config and enabled state
- `GET /api/mesh/observability/config` endpoint returns basic observability status
- New test file `Phase4Tests/MeshObservabilityTests.cs` — 9 tests covering router observability, service configuration, and trace context

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
- **SkillSdk NuGet package (`task_101`)**. New `Hercules.SkillSdk` class library exposing
  whitelist interfaces for file-based C# skills:
  `IHttpClient`, `IMcpClient`, `ILlmClient`, `IMemoryClient`, `ISkillLogger`,
  `ISessionContext`, and aggregate `IHerculesSkillContext`. Agent-side adapters
  enforce allowed domains, MCP tool allow-list, memory scopes, and session isolation.
- **SkillSdk in-process executor**. `SkillSdkExecutor` compiles SkillSdk C# skills in a
  collectible `AssemblyLoadContext` with Roslyn, injects `IHerculesSkillContext`, and
  routes `execute_code` calls automatically when code references `Hercules.SkillSdk`.
  Combines `DangerousCodeScanner` (blacklist) with `SkillSdkWhitelistScanner`
  (namespace/keyword whitelist) and metadata-only reference assemblies.
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
- **Security Operations (task_055)**: Fleet-wide identity rotation, credential revocation,
  certificate renewal, package signing verification, vulnerability reporting, and security audit export.
  - `IFleetIdentityService` / `FleetIdentityService`: Fleet-wide identity management with rotation
  - `ICertificateService` / `CertificateService`: X.509 certificate lifecycle management
  - `IPackageSigningService` / `PackageSigningService`: HMAC-SHA256 package signature verification
  - `IVulnerabilityReporter` / `VulnerabilityReporterService`: Vulnerability tracking and reporting
  - `ISecurityAuditExporter` / `SecurityAuditExporterService`: Security audit trail and compliance exports
  - `SecurityOpsConfig`: Unified configuration for all security operations
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
