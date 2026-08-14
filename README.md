<p align="center">
  <img src="assets/branding/logo.svg" alt="Hercules" width="560" />
</p>

<p align="center">
  <b>Self-improving AI agent on C# / .NET 10</b><br/>
  Creates skills from experience · improves them during use · remembers context between sessions · connects agents into a mesh
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="C#" src="https://img.shields.io/badge/C%23-15-239120?logo=csharp&logoColor=white" />
  <img alt="Astro" src="https://img.shields.io/badge/Astro-Frontend-FF5D01?logo=astro&logoColor=white" />
  <img alt="License" src="https://img.shields.io/badge/License-MIT-yellow.svg" />
  <img alt="Status" src="https://img.shields.io/badge/status-active-success.svg" />
</p>

<p align="center">
  <a href="README-EN.md"><b>English</b></a>
  · <a href="README-RU.md">Русский</a>
</p>

---

**Hercules** is a self-improving micro-agent that reproduces the key *self-improving* characteristics
of "hermes-style" agents (Nous Research) in a runnable form factor — and extends them with
sandboxed code execution, a tool ecosystem, multi-agent mesh networking, and operational hardening
for edge/IoT deployments.

The agent **creates skills from experience**, **improves them during use**, **retains knowledge across sessions**,
builds a deepening model of the user, **executes code safely**, **calls external tools**, and **forms mesh networks
with peer agents**. It supports **YandexGPT**, **Ollama Cloud**, and **Ollama Local**
through a single OpenAI-compatible interface (`Microsoft.Extensions.AI`).

> This is the default landing page (English). For the Russian version, see [README-RU.md](README-RU.md).

---

## ✨ Features

| Subsystem                       | What it does                                                                                                                        |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| **Self-Improving Skill System** | Automatically proposes creating a skill on repeated queries (>2 times), versions skills, and improves them when success rate is low |
| **Long-term Memory**            | Layered memory (working / episodic / durable facts); stores profile, preferences, entities, and session context in Markdown        |
| **Reflection Engine**           | Self-analysis after a session or every N commands — what went well / poorly / what to improve; includes sandbox audit trace analysis |
| **Skill Router**                | Semantic routing with embedding scorer, lexical scorer, schema compatibility, historical quality, and policy eligibility          |
| **Skill Marketplace**           | Package, sign, and validate skills; lifecycle management (create → evaluate → deprecate) with quality scoring                      |
| **Multi-Role LLM Routing**     | `main` / `code_writer` / `reflector` roles routed to different (provider, model, temperature) tuples                                |
| **Sandboxed Code Execution**    | C# file-based apps in a 3-layer sandbox: regex pre-scan → isolated temp dir → POSIX ulimit wrapper; network denied by default     |
| **Tool Ecosystem**              | LLM-callable tools: `http` (allow-list domains), `execute_code`, `a2a` (JSON-RPC 2.0), `mcp` (Model Context Protocol)             |
| **Agent Mesh**                  | Multi-agent mesh networking: capability registry, intent routing, circuit breaker, fan-out orchestration, verification pipeline    |
| **HerculesBus**                 | Internal event bus with in-memory, HTTP, and SQLite backends                                                                       |
| **Hybrid Storage**              | Files (Markdown + JSON) for skills and memory + SQLite for logs, metrics, and sandbox executions + Redis/NATS/PostgreSQL backends   |
| **Observability**               | OpenTelemetry integration: tracing, metrics, structured logs                                                                       |
| **Security Operations**         | Fleet identity rotation, X.509 certificates, package signing (HMAC-SHA256), vulnerability reporting, security audit export         |
| **Operational Hardening**       | Budget guardrails, rate limiting & quotas, audit logging, secret masking, privacy redaction, encrypted backup & recovery            |
| **Edge Provisioning**           | Raspberry Pi deployment with Docker (ARM64/Alpine), device enrollment, sensor simulation, fleet templates                          |
| **Offline Resilience**          | Outbox queue, network monitoring, sync-on-reconnect, deterministic fallback when LLM is unreachable                                  |
| **Config Hot-Reload**           | Change LLM providers, system prompts, and thresholds via Web UI or API without restart                                             |
| **Multi-provider LLM**          | YandexGPT (primary), Ollama Cloud / Local, LM Studio — through a single OpenAI-compatible interface with automatic fallback         |
| **Interfaces**                  | CLI (REPL, primary) + Telegram bot (secondary) + Web API (35 controllers) + Astro SPA                                              |

---

## 🏗️ Architecture

```
src/agent/                         # Main agent project
├── Program.cs                      # Entry point, DI, and configuration setup
├── appsettings.json                # Provider and agent thresholds configuration
├── Agent/                          # Agent core
│   ├── AgentCore.cs                # Main request-processing loop (tool-aware, multi-role)
│   ├── SkillRouter.cs              # Skill-based routing (PhraseReceivers + semantic scoring)
│   ├── SkillManager.cs             # Skill CRUD + versioning (via LLM)
│   ├── ReflectionEngine.cs         # Self-analysis, sandbox audit, reflection reports
│   ├── MemoryManager.cs            # Long-term memory, user model
│   └── WebApiAdapter.cs            # Core adapter for Web API + DTO
├── LLM/                            # LLM layer (Microsoft.Extensions.AI)
│   ├── ILLMClient.cs               # Unified provider interface (multi-role)
│   ├── ChatClientLLMClient.cs      # Base over IChatClient
│   ├── YandexGPTClient.cs          # YandexGPT (OpenAI-compatible endpoint)
│   ├── LocalLLMClient.cs           # Ollama Cloud/Local, LM Studio
│   ├── LlmClientFactory.cs         # Client factory by provider name
│   ├── ResilientLLMClient.cs       # Resilience + fallback chain + role routing
│   ├── RoleRouter.cs               # Role → ILLMClient resolver
│   ├── ProviderHealthChecker.cs    # Provider health monitoring
│   ├── ProviderCapabilityDetector.cs # Provider capability detection
│   ├── JsonRepair/                 # JSON response repair utilities
│   └── Providers/                 # OpenAI-compatible, LM Studio clients
├── Skills/                         # Skill lifecycle & marketplace
│   ├── SkillMarketplace.cs         # Package, sign, validate, distribute skills
│   ├── SkillPackager.cs            # Skill packaging engine
│   ├── SkillLifecycleService.cs    # Create → evaluate → deprecate lifecycle
│   ├── SkillEvaluationEngine.cs    # Quality scoring and evaluation harness
│   ├── SkillDeprecationManager.cs  # Graceful skill deprecation
│   ├── AgentTemplateManager.cs     # IoT template management
│   ├── EmbeddingSkillRouter.cs    # Semantic skill routing
│   ├── Eval/ Quality/ Package/ Routing/  # Sub-modules
├── CodeExecution/                  # Sandboxed code execution (v2 Stage 2)
│   ├── ICodeExecutor.cs            # Contract
│   ├── DotnetFileBasedExecutor.cs  # dotnet run --file implementation
│   ├── DangerousCodeScanner.cs     # Pre-execution regex scan (25+ patterns)
│   └── SandboxOptions.cs           # ulimit + timeouts + allow-list
├── Tools/                          # Tool ecosystem (v2 Stage 3)
│   ├── ITool.cs                    # Tool contract
│   ├── ToolRegistry.cs             # LLM prompt injection
│   ├── HttpTool.cs                 # GET/POST/PUT/DELETE + allow-list
│   ├── A2AClient.cs                # JSON-RPC 2.0 agent-to-agent
│   ├── McpClient.cs                # Model Context Protocol client
│   ├── CodeExecutionTool.cs        # ICodeExecutor → ITool adapter
│   ├── WasmToolAdapter.cs         # WASM tool bridge
│   └── Approval/ Grants/ Policy/ Registry/  # Tool governance
├── Mesh/                            # Inter-agent mesh (36+ sub-modules)
│   ├── AgentManifest.cs            # Agent identity and capabilities
│   ├── CapabilityRegistry.cs       # Peer capability tracking
│   ├── MeshRouter.cs               # Intent-based mesh routing
│   ├── IntentRouter.cs             # Delegation with observability
│   ├── CircuitBreaker.cs           # Resilience for peer calls
│   ├── SharedMemorySync.cs         # Inter-agent memory synchronization
│   ├── DistributedReflection.cs    # Cross-agent reflection
│   ├── A2A/ Abstractions/ Aggregation/ Audit/ Auth/ Backend/
│   ├── Backends/ Budget/ Dashboard/ Discovery/ Escalation/
│   ├── Eval/ Observability/ Policy/ Profiles/ Resilience/
│   ├── Router/ Schema/ Transport/ Verification/
├── Mcp/                             # Model Context Protocol
│   ├── McpClientService.cs          # MCP client connection management
│   ├── McpServerHost.cs            # MCP server hosting
│   ├── HerculesMcpServerTool.cs    # Expose agent tools via MCP
│   └── McpToolAdapter.cs           # MCP ↔ ITool bridge
├── WasmSandbox/                     # WASM-based sandbox
│   ├── IWasmSandbox.cs              # Contract
│   ├── WasmtimeSandbox.cs           # Wasmtime runtime (NuGet: Wasmtime 44.0.0)
│   ├── WasmTool.cs                  # WASM → ITool adapter
│   └── Compilation/                # C# and passthrough compilers
├── Security/                        # Fleet security operations
│   ├── Fleet identity rotation, certificates, package signing,
│   │   vulnerability reporting, audit export
├── Edge/                            # Raspberry Pi edge provisioning
├── Offline/                         # Offline resilience (outbox, sync)
├── Degradation/                     # Local-first degradation + deterministic fallback
├── Backup/                          # Encrypted backup & recovery
├── Slo/                             # Operational SLOs
├── Quotas/                          # Rate limiting & quotas
├── Observability/                   # OpenTelemetry (tracing, metrics, logs)
├── Reflection/                      # Self-improvement service + proposal store
├── Context/                         # Context assembly + trace summarization
├── Cache/                           # Unified cache with sensitivity levels
├── Budget/                          # Budget guardrails
├── Audit/                           # Audit logging + payload hashing
├── Redaction/                        # Privacy redaction
├── Config/                          # Configuration models + rollout
│   ├── AppConfig.cs                 # Full config (Llm/Storage/Agent/CodeExecution/Http/Mcp/A2A/Roles/Mesh/...)
│   ├── SecurityOpsConfig.cs
│   ├── SecretMaskingService.cs
│   ├── RuntimeConfigStore.cs
│   └── Rollout/                     # Staged config bundles
├── Contracts/                      # API/DTO contracts
├── Storage/                         # Hybrid storage
│   ├── FileSkillRepository.cs       # Skills/ — skill files
│   ├── MemoryStore.cs              # Memory/ — Markdown memory
│   ├── SqliteSessionStore.cs        # SQLite: sessions, logs, metrics, sandbox_executions
│   ├── BudgetService.cs
│   └── AuditLogService.cs
├── Memory/                          # Layered memory system
│   └── Layers/ (WorkingMemory, DurableFacts, Episodic)
├── HerculesBus/                     # Internal event bus
│   ├── Bus.cs                       # Pub/sub core
│   └── Core/ Http/ InMemory/ Sqlite/  # Multiple backends
├── Tasks/                           # Durable task lifecycle
├── Lifecycle/                       # Agent lifecycle management
├── Simulation/                      # Template simulation (sensor simulators, failure scenarios)
├── Fleet/                           # Fleet template management
├── Loop/                            # Loop utilities
├── CLI/
│   ├── ConsoleUI.cs                 # REPL loop (Spectre.Console)
│   └── BenchmarkRunner.cs
├── Telegram/
│   └── TelegramBot.cs              # Telegram bot (long polling)
└── data/                            # Runtime data (git-ignored)
    ├── Skills/ Memory/ sessions.db  runtime-config.json
```

Additional projects:

```
src/agent/Hercules.WebApi/           # ASP.NET Core Minimal API (REST), port :5000
├── Program.cs                         # DI + CORS + middleware, reuses the core
├── Auth/                              # ApiKeyMiddleware, RateLimitMiddleware,
│                                      # RequestBodyLimitMiddleware, PeerAuthMiddleware
├── Config/                            # WebApiConfig + RuntimeConfigHostedService (hot-reload)
└── Controllers/                       # 35 controllers: Chat, Skills, SkillLifecycle,
                                       # SkillQuality, SkillHarness, SkillManifest,
                                       # Marketplace, Memory, Stats, Config, Rollout,
                                       # Mesh, MeshProfiles, MeshObservability, Lifecycle,
                                       # Budget, Quotas, Audit, Llm, A2A, Backups,
                                       # FleetTemplates, Grants, Simulation, SLOs,
                                       # Approvals, Escalations, Observability,
                                       # SelfImprovement, TaskProgress, Context, Cache,
                                       # ToolRegistry, MCP, Template

src/hercules-web/                    # Astro + TailwindCSS frontend, port :4321
├── src/lib/api.ts                    # Typed Web API client (35+ endpoints)
├── src/layouts/Layout.astro         # Base layout (dark theme, navigation)
├── src/components/                  # ChatBox, SkillCard, ProfileEditor, ConfigEditor,
│                                    # StatsDashboard, MeshDashboard, MeshRouterPanel,
│                                    # EscalationPanel
└── src/pages/                       # index / skills / profile / stats / config / memmesh
```

Deployment:

```
deploy/raspberry-pi/                # Edge deployment for Raspberry Pi
├── Dockerfile                      # Multi-stage ARM64 build (Alpine, non-root)
├── docker-compose.yml              # Host networking, privileged (GPIO/Wi-Fi)
├── appsettings.edge.json           # Edge-optimized config
├── first-boot.sh                   # Boot provisioning script
└── provision.env.template         # Environment template for credentials

templates/                          # IoT scenario templates
├── greenhouse/                     # Greenhouse monitoring & control
├── vending/                        # Vending machine fleet management
├── server-room/                    # Server room monitoring
└── cold-chain/                     # Cold chain logistics monitoring
```

All runtime data goes into the `data/` directory:

```
data/
├── Skills/                    # skill.{id}.md / .prompt.md / .meta.json / .usage.json / .v{N}.md
├── Memory/                    # user_profile.md, preferences.md, entities.md, context_{date}.md
├── Templates/                 # IoT agent templates (.agenttemplate)
├── FleetTemplates/            # Fleet deployment templates (.fleettemplate)
├── runtime-config.json        # Hot-reloadable configuration (via /config API)
└── sessions.db                # SQLite: sessions, interactions, metrics, sandbox_executions
```

---

## 🚀 Installation and Run

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 22+](https://nodejs.org/) (for the web frontend)

### Build

```bash
dotnet restore
dotnet build
```

### Publish (for deployment)

```bash
# Backend
dotnet publish src/agent/Hercules.WebApi -c Release -o ./dist/webapi

# Frontend
cd src/hercules-web
npm install
npm run build   # static output in src/hercules-web/dist
```

After publishing, run `dist/webapi/Hercules.WebApi` and serve frontend static files
(e.g., `npx serve src/hercules-web/dist -p 4321`). All user data (skills, memory, DB, runtime
config) lives in the `data/` directory, which can be kept outside the repository.

### Run CLI (primary mode)

```bash
dotnet run --project src/agent/Hercules
```

### Run Telegram bot

```bash
dotnet run --project src/agent/Hercules -- --telegram
```

(set `Telegram:BotToken` in `appsettings.json` beforehand)

### Run Web API (REST server)

```bash
dotnet run --project src/agent/Hercules.WebApi
```

The server starts on `http://localhost:5000`. The agent core (`AgentCore`) is reused
through the `WebApiAdapter` adapter — there is no separate agent logic in the Web API.

### Run CLI via the main project

```bash
dotnet run --project src/agent/Hercules -- --cli   # REPL mode
dotnet run --project src/agent/Hercules            # same thing (CLI by default)
```

---

## 🌐 Web API

ASP.NET Core Minimal API with 35 controllers. All responses are JSON (UTF-8, camelCase).
Protection is the `X-Api-Key` header (value from `WebApi:ApiKey`, default `dev-local-key`).
CORS is open for the local frontend (`http://localhost:4321`, `http://localhost:3000`).
Every interaction is logged in SQLite (`data/sessions.db`).

### Core endpoints

| Method  | Route                      | Description                                                    |
| ------- | -------------------------- | -------------------------------------------------------------- |
| `GET`   | `/api/health`              | Liveness check (no key required)                               |
| `POST`  | `/api/chat`                | Send a message to the agent → response + mode/confidence/skill |
| `GET`   | `/api/skills`              | List skills                                                    |
| `POST`  | `/api/skills`              | Create a skill manually; `?ai=true` — generate via LLM         |
| `GET`   | `/api/skills/{id}`         | Skill details (metadata + prompt)                              |
| `PUT`   | `/api/skills/{id}`         | Update a skill (triggers/prompt/description) → new version     |
| `POST`  | `/api/skills/{id}/improve` | Improve a skill via LLM → new version                          |
| `GET`   | `/api/memory/profile`      | Long-term memory profile (Markdown)                             |
| `PUT`   | `/api/memory/profile`      | Overwrite the memory profile                                   |
| `POST`  | `/api/memory/reset`        | Reset long-term memory                                         |
| `GET`   | `/api/reflect`             | Run reflection → Markdown report                               |
| `GET`   | `/api/stats`               | Metrics: total, skill/direct, success rate, per-day            |
| `GET`   | `/api/config`              | Current runtime configuration                                  |
| `PUT`   | `/api/config`              | Full configuration replacement                                 |
| `PATCH` | `/api/config`              | Partial configuration update (merge patch)                     |

### Extended endpoints

| Method  | Route                                    | Description                                          |
| ------- | ---------------------------------------- | ---------------------------------------------------- |
| `GET`   | `/api/skills/{id}/lifecycle`             | Skill lifecycle status                               |
| `POST`  | `/api/skills/{id}/lifecycle/deprecate`   | Deprecate a skill                                   |
| `GET`   | `/api/skills/{id}/quality`               | Skill quality score                                 |
| `GET`   | `/api/marketplace`                        | Browse skill marketplace                             |
| `POST`  | `/api/marketplace/publish`               | Publish a skill to marketplace                       |
| `GET`   | `/api/mesh/status`                       | Mesh network status                                 |
| `GET`   | `/api/mesh/peers`                        | List connected mesh peers                           |
| `POST`  | `/api/mesh/delegate`                     | Delegate a task to a peer agent                     |
| `GET`   | `/api/mesh/observability/status`         | Mesh observability config                           |
| `GET`   | `/api/budget`                            | Current budget status                                |
| `GET`   | `/api/quotas`                            | Quota usage                                         |
| `GET`   | `/api/audit`                             | Audit log entries                                    |
| `GET`   | `/api/backups`                            | List backups                                        |
| `POST`  | `/api/backups`                            | Create a backup                                     |
| `POST`  | `/api/backups/{id}/restore`              | Restore from a backup                                |
| `GET`   | `/api/slos`                              | SLO compliance status                               |
| `GET`   | `/api/lifecycle`                         | Agent lifecycle status                              |
| `GET`   | `/api/tasks/{id}/progress`               | Durable task progress                               |
| `GET`   | `/api/context`                            | Current context assembly                             |
| `DELETE`| `/api/cache`                             | Clear cache                                          |
| `GET`   | `/api/tools`                             | List available tools                                 |
| `POST`  | `/api/tools/{name}/execute`              | Execute a tool                                       |
| `GET`   | `/api/a2a/status`                        | A2A (agent-to-agent) status                         |
| `GET`   | `/api/rollout/status`                    | Config rollout status                               |
| `GET`   | `/api/self-improvement/proposals`        | Self-improvement proposals                          |
| `POST`  | `/api/simulations/{template}`            | Run a template simulation                           |

Example:

```bash
curl -X POST http://localhost:5000/api/chat \
  -H "X-Api-Key: dev-local-key" -H "Content-Type: application/json" \
  -d '{"message":"what is the weather in Moscow?"}'
```

Web API configuration (`src/agent/Hercules.WebApi/appsettings.json`):

```jsonc
"WebApi": {
  "ApiKey": "dev-local-key",                  // empty string → no-key access
  "AllowedCorsOrigins": [ "http://localhost:4321", "http://localhost:3000" ]
}
```

---

## 🎨 Web Interface (Astro)

Minimalist SPA on **Astro + TailwindCSS** (dark theme, monospaced code blocks).
Located in the `src/hercules-web/` directory.

| Page       | Purpose                                                                           |
| ---------- | --------------------------------------------------------------------------------- |
| `/`        | Chat with the agent (mode/confidence/provider badges, typing effect, skill hints) |
| `/skills`  | Skill list, manual creation and AI improvement, editing, lifecycle & quality      |
| `/profile` | Long-term memory profile editor + reset                                           |
| `/config`  | **Agent configuration editor** — LLM providers, system prompt, thresholds, tools  |
| `/stats`   | Metrics dashboard, skill/direct ratio, daily activity, reflection                 |
| `/memmesh` | Mesh dashboard — peer agents, routing, observability                              |

Components: `ChatBox`, `SkillCard`, `ProfileEditor`, `ConfigEditor`, `StatsDashboard`,
`MeshDashboard`, `MeshRouterPanel`, `EscalationPanel`. API client — `src/lib/api.ts`.

### Run the frontend

```bash
cd src/hercules-web
npm install
npm run dev        # dev server on http://localhost:4321
```

The backend address and key are configured via environment variables (`src/hercules-web/.env`):

```bash
PUBLIC_API_BASE=http://localhost:5000
PUBLIC_API_KEY=dev-local-key
```

> **Hot-reload configuration**: no need to edit `appsettings.json` before launch.
> Open `/config`, paste LLM provider keys, and save — settings apply immediately
> without server restart, persisted in `data/runtime-config.json`.

### Full local run (two terminals)

```bash
# Terminal 1 — backend
dotnet run --project src/agent/Hercules.WebApi      # → :5000

# Terminal 2 — frontend
cd src/hercules-web && npm run dev                  # → :4321
```

Open `http://localhost:4321`.

---

## ⚙️ Configuration (`appsettings.json`)

```jsonc
{
  "Llm": {
    "Provider": "yandexgpt",                 // active provider
    "Fallback": ["ollama-cloud", "ollama-local"], // fallback order
    "Roles": {                               // v2: multi-role routing
      "main": { "Provider": "yandexgpt", "Model": "yandexgpt", "Temperature": 0.6 },
      "code_writer": { "Provider": "ollama-local", "Model": "codellama", "Temperature": 0.2 },
      "reflector": { "Provider": "ollama-local", "Model": "llama3.1", "Temperature": 0.8 }
    },
    "YandexGpt": {
      "Endpoint": "https://llm.api.cloud.yandex.net/v1",
      "ApiKey": "<IAM or API key>",
      "FolderId": "<Yandex Cloud folder id>",
      "Model": "yandexgpt",                  // becomes gpt://{folderId}/yandexgpt/latest
      "Temperature": 0.6,
      "MaxTokens": 2000
    },
    "OllamaCloud": {
      "Endpoint": "https://ollama.com/v1",
      "ApiKey": "<Ollama Cloud key>",
      "Model": "gpt-oss:120b"
    },
    "OllamaLocal": {
      "Endpoint": "http://localhost:11434/v1",
      "ApiKey": "",                          // no key required locally
      "Model": "llama3.1"
    }
  },
  "Agent": {
    "SkillCreationThreshold": 3,             // repetitions before proposing a skill
    "SkillImprovementThreshold": 0.6,        // success_rate threshold for improvement
    "SkillEvaluationWindow": 5,              // skill evaluation window
    "ReflectionEveryNCommands": 10           // auto-reflection every N commands
  },
  "CodeExecution": {                         // v2: sandboxed code execution
    "Enabled": true,
    "TimeoutSeconds": 30,
    "MaxFileSizeBytes": 10485760,             // 10 MB
    "AllowNetwork": false,
    "MaxCodeSizeBytes": 102400                // 100 KB
  },
  "Http": {                                  // v2: HTTP tool config
    "AllowedDomains": ["*"],
    "RequestsPerMinute": 60,
    "TimeoutSeconds": 10
  },
  "Mesh": {                                  // inter-agent mesh
    "Enabled": false,
    "Transport": "http",                      // http | grpc | nats
    "Discovery": "manual",                     // manual | mdns | consul
    "PeerAuth": { "Enabled": false }
  },
  "Telegram": { "Enabled": false, "BotToken": "" }
}
```

> Any setting can be overridden via environment variables with the `HERCULES_` prefix,
> for example: `HERCULES_Llm__Provider=ollama-local`.
>
> In Web mode, configuration can also be changed via the UI (`/config`) or API
> (`PUT`/`PATCH /api/config`) — changes are saved to `data/runtime-config.json`
> and applied without server restart.

### LLM Providers

All providers work through an **OpenAI-compatible interface** and the
`Microsoft.Extensions.AI` abstraction (`IChatClient`). Supported providers:

- **YandexGPT** — primary (Russia). The model is passed as `gpt://{folderId}/{model}/latest`.
- **Ollama Cloud** — cloud fallback (`https://ollama.com/v1`).
- **Ollama Local / LM Studio** — local fallback (`http://localhost:11434/v1`).

If the primary provider is unavailable, `ResilientLLMClient` automatically switches
to the next one in the `Fallback` list. Each role (`main`, `code_writer`, `reflector`)
can use a different provider and model.

### Storage Backends

In addition to the default file + SQLite storage, Hercules supports:

- **SQLite** — sessions, logs, metrics, sandbox executions, audit, tasks
- **Redis / Valkey** — HerculesBus backend, caching (`StackExchange.Redis`)
- **NATS + JetStream** — mesh transport and event bus (`NATS.Client`)
- **PostgreSQL** — alternative persistent backend (`Npgsql`)
- **gRPC** — mesh transport (`Grpc.Net.Client`)

---

## 💻 CLI Commands

| Command                 | Description                                   |
| ----------------------- | --------------------------------------------- |
| `> text`                | Direct query to LLM with profile context      |
| `/skills`               | Show all skills (table)                        |
| `/skills create "name"` | Create a skill manually                       |
| `/skills improve {id}` | Improve a skill (new version)                 |
| `/memory show`          | Show user profile                             |
| `/memory reset`         | Reset memory                                  |
| `/reflect`              | Run reflection manually                       |
| `/help`                 | Help                                          |
| `/exit`                 | Exit with context saving and final reflection |

## 🤖 Telegram Commands

- `/start` — initialization
- `/skills` — list of skills
- `/profile` — what the agent knows about the user
- `/reset` — reset memory
- plain text — agent response

---

## 🔄 How the self-improving cycle works

1. **Request** → load profile and context from memory
2. **Routing** → find a matching skill by PhraseReceivers + semantic scoring (`SkillRouter`)
3. **LLM response** → with the active skill (skill-prompt) or directly (direct)
4. **Tool execution** → if LLM output contains a tool action, execute and feed result back (up to 3 iterations)
5. **Logging** → input/output/confidence/mode in SQLite
6. **Skill threshold** → if the request has been repeated `SkillCreationThreshold` times → propose creating a skill (with confirmation)
7. **Improvement threshold** → if `success_rate < SkillImprovementThreshold` → propose updating the skill
8. **Memory saving** → facts about the user, entities, preferences
9. **Reflection** → at the end of a session or every N commands; includes sandbox audit trace analysis

### Principles

- **Never stop learning** — every session enriches memory or skills
- **Explicit improvement loop** — the agent itself proposes fixes
- **Transparent** — the user sees all creations/improvements
- **Human-in-the-loop** — skills are created only after confirmation
- **Versioned** — old skill versions are not deleted (`skill.{id}.v{N}.md`)
- **Safe by default** — code execution is sandboxed; tools require allow-lists and approval
- **Observable** — OpenTelemetry traces, metrics, and structured logs throughout

---

## 🧪 Testing

```bash
dotnet test                          # Run all unit tests
dotnet test --filter "Phase2Tests"   # Run v2 feature tests
dotnet test --filter "Phase3Tests"   # Run v3 feature tests
dotnet test --filter "Phase4Tests"   # Run mesh observability tests
```

Test project: `tests/Hercules.Agent.Tests/` (xUnit + Moq, 34 test directories).

### Acceptance Criteria Check

| Criterion                        | How to verify                                              |
| -------------------------------- | ---------------------------------------------------------- |
| A skill is created automatically | Repeat the same query 3 times → the agent proposes a skill |
| A skill is used                  | After creation — the query goes through `skill: ...`       |
| A skill improves                 | After a series of bad responses → a proposal to update     |
| Profile is saved                 | Restart → `/memory show` remembers facts                   |
| Context is carried over          | Session 1: fact → Session 2: agent remembers               |
| Reflection runs                  | After `/exit` — Reflection Engine output                   |
| Code executes safely             | `execute_code` tool → sandbox blocks dangerous patterns   |
| Tools are callable               | `http` tool → allow-listed HTTP request                    |
| Mesh delegates tasks             | Configure peers → `POST /api/mesh/delegate`               |
| Config hot-reloads               | Change via `/config` → applied without restart             |

---

## 🐳 Docker (Raspberry Pi Edge)

```bash
cd deploy/raspberry-pi
cp provision.env.template provision.env   # fill in credentials
docker compose up -d
```

See `deploy/raspberry-pi/` for the Dockerfile (ARM64/Alpine, non-root user), edge-optimized
config, and first-boot provisioning script.

---

## 📦 Dependencies (NuGet)

| Package | Version | Purpose |
| ------- | ------- | ------- |
| `Microsoft.Extensions.AI` + `OpenAI` | 10.9.0 | AI abstractions + OpenAI-compatible SDK |
| `Microsoft.Extensions.Hosting` | 10.0.11 | DI, hosting, configuration |
| `Microsoft.Data.Sqlite` | 10.0.11 | SQLite for sessions, metrics, audit |
| `Wasmtime` | 44.0.0 | WASM sandbox runtime |
| `ModelContextProtocol` | 2.2.0 | MCP server/client |
| `YamlDotNet` | 18.1.0 | YAML front-matter parsing |
| `Spectre.Console` | 0.57.2 | CLI interface |
| `Telegram.Bot` | 22.10.2.1 | Telegram interface |
| `Grpc.Net.Client` | 2.83.0 | gRPC mesh transport |
| `StackExchange.Redis` | 3.1.13 | Redis/Valkey backend |
| `NATS.Client` | 3.1.0 | NATS JetStream + KV backend |
| `Npgsql` | 10.0.3 | PostgreSQL backend |
| `OpenTelemetry` | 1.17.0 | Observability (tracing + metrics) |

Frontend: **Astro 6.4+**, **TailwindCSS 4.3+**, **Node.js 22.12+**

---

## 📚 Documentation

> All documents are available in two languages. The default links point to the English version.

| Document                                                                              | Description                     |
| ------------------------------------------------------------------------------------- | ------------------------------- |
| [docs/QUICKSTART-EN.md](docs/QUICKSTART-EN.md) · [RU](docs/QUICKSTART-RU.md)          | Quick start in a few minutes    |
| [docs/ARCHITECTURE-EN.md](docs/ARCHITECTURE-EN.md) · [RU](docs/ARCHITECTURE-RU.md)    | Core and interface architecture |
| [docs/AGENT-MESH-EN.md](docs/AGENT-MESH-EN.md) · [RU](docs/AGENT-MESH-RU.md)          | Micro-agent mesh concept        |
| [docs/IOT-SCENARIOS-EN.md](docs/IOT-SCENARIOS-EN.md) · [RU](docs/IOT-SCENARIOS-RU.md)| B2C/B2B IoT deployment scenarios|
| [docs/ROADMAP-EN.md](docs/ROADMAP-EN.md) · [RU](docs/ROADMAP-RU.md)                   | Planned features and milestones |
| [docs/CONFIGURATION-EN.md](docs/CONFIGURATION-EN.md) · [RU](docs/CONFIGURATION-RU.md) | Full settings reference         |
| [docs/API-EN.md](docs/API-EN.md) · [RU](docs/API-RU.md)                               | REST Web API reference          |
| [CONTRIBUTING-EN.md](CONTRIBUTING-EN.md) · [RU](CONTRIBUTING-RU.md)                   | How to contribute               |
| [CHANGELOG-EN.md](CHANGELOG-EN.md) · [RU](CHANGELOG-RU.md)                            | Change history                  |
| [SECURITY.md](SECURITY.md)                                                            | Security policy                 |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)                                              | Code of conduct                 |

---

## 🤝 Contributing

PRs and Issues are welcome! Before starting, please read [CONTRIBUTING-EN.md](CONTRIBUTING-EN.md)
and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Report vulnerabilities via [SECURITY.md](SECURITY.md).

---

## 📝 License

[MIT](LICENSE) © 2026 Victor Buzin.