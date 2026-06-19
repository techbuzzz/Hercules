<p align="center">
  <img src="assets/branding/logo.svg" alt="Hercules" width="560" />
</p>

<p align="center">
  <b>Self-improving AI agent on C# / .NET 10</b><br/>
  Creates skills from experience · improves them during use · remembers context between sessions
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="C#" src="https://img.shields.io/badge/C%23-15-239120?logo=csharp&logoColor=white" />
  <img alt="Astro" src="https://img.shields.io/badge/Astro-Frontend-FF5D01?logo=astro&logoColor=white" />
  <img alt="License" src="https://img.shields.io/badge/License-MIT-yellow.svg" />
  <img alt="Status" src="https://img.shields.io/badge/status-active-success.svg" />
</p>

---

**Hercules** is a compact self-improving micro-agent that reproduces the key *self-improving* characteristics
of "hermes-style" agents (Nous Research) in a runnable form factor.

The agent **creates skills from experience**, **improves them during use**, **retains knowledge across sessions**,
and builds a deepening model of the user. It supports **YandexGPT**, **Ollama Cloud**, and **Ollama Local**
through a single OpenAI-compatible interface (`Microsoft.Extensions.AI`).

---

## ✨ Features

| Subsystem                       | What it does                                                                                                                        |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| **Self-Improving Skill System** | Automatically proposes creating a skill on repeated queries (>2 times), versions skills, and improves them when success rate is low |
| **Long-term Memory**            | Stores user profile, preferences, entities, and session context in Markdown; carries context across restarts                        |
| **Reflection Engine**           | Self-analysis after a session or every N commands — what went well / poorly / what to improve                                       |
| **Skill Router**                | Query routing: skill by triggers or direct LLM response                                                                             |
| **Hybrid Storage**              | Files (Markdown + JSON) for skills and memory + SQLite for logs and metrics                                                         |
| **Multi-provider LLM**          | YandexGPT (primary), Ollama Cloud / Local, LM Studio — through a single OpenAI-compatible interface with automatic fallback         |
| **Interfaces**                  | CLI (REPL, primary) + Telegram bot (secondary)                                                                                      |

---

## 🏗️ Architecture

```
Hercules/
├── Program.cs                 # Entry point, DI and configuration setup
├── appsettings.json           # Provider and agent thresholds configuration
├── Config/
│   └── AppConfig.cs           # Configuration models
├── Agent/                     # Agent core
│   ├── AgentCore.cs           # Main request-processing loop
│   ├── SkillRouter.cs         # Skill-based routing (triggers)
│   ├── SkillManager.cs        # Skill CRUD + versioning (via LLM)
│   ├── ReflectionEngine.cs    # Self-analysis, reflection reports
│   └── MemoryManager.cs       # Long-term memory, user model
├── LLM/                       # LLM layer (Microsoft.Extensions.AI)
│   ├── ILLMClient.cs          # Unified provider interface
│   ├── ChatClientLLMClient.cs # Base over IChatClient
│   ├── YandexGPTClient.cs     # YandexGPT (OpenAI-compatible endpoint)
│   ├── LocalLLMClient.cs      # Ollama Cloud/Local, LM Studio
│   ├── LlmClientFactory.cs    # Client factory by provider name
│   └── ResilientLLMClient.cs  # Resilience + fallback chain
├── Storage/                   # Storage
│   ├── FileSkillRepository.cs # Skills/ — skill files
│   ├── MemoryStore.cs         # Memory/ — Markdown memory
│   ├── SqliteSessionStore.cs  # SQLite: sessions, logs, metrics, counters
│   └── Models.cs              # Domain models (Skill, SkillMeta, ...)
├── CLI/
│   └── ConsoleUI.cs           # REPL loop (Spectre.Console)
├── Telegram/
│   └── TelegramBot.cs         # Telegram bot (long polling)
└── Agent/WebApiAdapter.cs     # Core adapter for Web API + DTO
```

Additional "façade" projects on top of the core:

```
Hercules.WebApi/            # ASP.NET Core Minimal API (REST), port :5000
├── Program.cs                 # DI + CORS + middleware, reuses the core
├── Auth/ApiKeyMiddleware.cs   # X-Api-Key header validation
├── Config/WebApiConfig.cs     # API key + allowed CORS origins
└── Controllers/               # Chat / Skills / Memory / Stats (Minimal API)

hercules-web/                    # Astro + TailwindCSS frontend, port :4321
├── src/lib/api.ts             # Web API client
├── src/layouts/Layout.astro   # Base layout (dark theme, navigation)
├── src/components/            # ChatBox, SkillCard, ProfileEditor, StatsDashboard
└── src/pages/                 # index / skills / profile / stats
```

All runtime data goes into the `data/` directory:

```
data/
├── Skills/                    # skill.{id}.md / .prompt.md / .meta.json / .usage.json / .v{N}.md
├── Memory/                    # user_profile.md, preferences.md, entities.md, context_{date}.md
└── sessions.db                # SQLite: sessions, interactions, metrics
```

---

## 🚀 Installation and Run

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build

```bash
cd Hercules
dotnet restore
dotnet build
```

### Run CLI (primary mode)

```bash
dotnet run
```

### Run Telegram bot

```bash
dotnet run -- --telegram
```

(set `Telegram:BotToken` in `appsettings.json` beforehand)

### Run Web API (REST server)

```bash
# from repository root
dotnet run --project Hercules.WebApi
```

The server starts on `http://localhost:5000`. The agent core (`AgentCore`) is reused
through the `WebApiAdapter` adapter — there is no separate agent logic in the Web API.

### Run CLI via the main project

```bash
dotnet run --project Hercules -- --cli   # REPL mode
dotnet run --project Hercules            # same thing (CLI by default)
```

---

## 🌐 Web API

ASP.NET Core Minimal API. All responses are JSON (UTF-8, camelCase). Protection is the
`X-Api-Key` header (value from `WebApi:ApiKey`, default `dev-local-key`). CORS is open for
the local frontend (`http://localhost:4321`, `http://localhost:3000`). Every interaction
is logged in SQLite (`data/sessions.db`).

| Method | Route                      | Description                                                    |
| ------ | -------------------------- | -------------------------------------------------------------- |
| `GET`  | `/api/health`              | Liveness check (no key required)                               |
| `POST` | `/api/chat`                | Send a message to the agent → response + mode/confidence/skill |
| `GET`  | `/api/skills`              | List skills                                                    |
| `POST` | `/api/skills`              | Create a skill manually; `?ai=true` — generate via LLM         |
| `GET`  | `/api/skills/{id}`         | Skill details (metadata + prompt)                              |
| `PUT`  | `/api/skills/{id}`         | Update a skill (triggers/prompt/description) → new version     |
| `POST` | `/api/skills/{id}/improve` | Improve a skill via LLM → new version                          |
| `GET`  | `/api/memory/profile`      | Long-term memory profile (Markdown)                            |
| `PUT`  | `/api/memory/profile`      | Overwrite the memory profile                                   |
| `POST` | `/api/memory/reset`        | Reset long-term memory                                         |
| `GET`  | `/api/reflect`             | Run reflection → Markdown report                               |
| `GET`  | `/api/stats`               | Metrics: total, skill/direct, success rate, per-day            |

Example:

```bash
curl -X POST http://localhost:5000/api/chat \
  -H "X-Api-Key: dev-local-key" -H "Content-Type: application/json" \
  -d '{"message":"what is the weather in Moscow?"}'
```

Web API configuration (`Hercules.WebApi/appsettings.json`):

```jsonc
"WebApi": {
  "ApiKey": "dev-local-key",                  // empty string → no-key access
  "AllowedCorsOrigins": [ "http://localhost:4321", "http://localhost:3000" ]
}
```

---

## 🎨 Web Interface (Astro)

Minimalist SPA on **Astro + TailwindCSS** (dark theme, monospaced code blocks).
Located in the `hercules-web/` directory.

| Page       | Purpose                                                                           |
| ---------- | --------------------------------------------------------------------------------- |
| `/`        | Chat with the agent (mode/confidence/provider badges, typing effect, skill hints) |
| `/skills`  | Skill list, manual creation and AI improvement, editing                           |
| `/profile` | Long-term memory profile editor + reset                                           |
| `/stats`   | Metrics dashboard, skill/direct ratio, daily activity, reflection                 |

Components: `ChatBox`, `SkillCard`, `ProfileEditor`, `StatsDashboard`. API client — `src/lib/api.ts`.

### Run the frontend

```bash
cd hercules-web
npm install
npm run dev        # dev server on http://localhost:4321
```

The backend address and key are configured via environment variables (`hercules-web/.env` file):

```bash
PUBLIC_API_BASE=http://localhost:5000
PUBLIC_API_KEY=dev-local-key
```

### Full local run (two terminals)

```bash
# Terminal 1 — backend
dotnet run --project Hercules.WebApi      # → :5000

# Terminal 2 — frontend
cd hercules-web && npm run dev                  # → :4321
```

Open `http://localhost:4321`.

---

## ⚙️ Configuration (`appsettings.json`)

```jsonc
{
  "Llm": {
    "Provider": "yandexgpt",                 // active provider
    "Fallback": ["ollama-cloud", "ollama-local"], // fallback order
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
  "Telegram": { "Enabled": false, "BotToken": "" }
}
```

> Any setting can be overridden via environment variables with the `HERCULES_` prefix,
> for example: `HERCULES_Llm__Provider=ollama-local`.

### LLM Providers

All providers work through an **OpenAI-compatible interface** and the
`Microsoft.Extensions.AI` abstraction (`IChatClient`). Supported providers:

- **YandexGPT** — primary (Russia). The model is passed as `gpt://{folderId}/{model}/latest`.
- **Ollama Cloud** — cloud fallback (`https://ollama.com/v1`).
- **Ollama Local / LM Studio** — local fallback (`http://localhost:11434/v1`).

If the primary provider is unavailable, `ResilientLLMClient` automatically switches
to the next one in the `Fallback` list.

---

## 💻 CLI Commands

| Command                 | Description                                   |
| ----------------------- | --------------------------------------------- |
| `> text`                | Direct query to LLM with profile context      |
| `/skills`               | Show all skills (table)                       |
| `/skills create "name"` | Create a skill manually                       |
| `/skills improve {id}`  | Improve a skill (new version)                 |
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
2. **Routing** → find a matching skill by triggers (`SkillRouter`)
3. **LLM response** → with the active skill (skill-prompt) or directly (direct)
4. **Logging** → input/output/confidence/mode in SQLite
5. **Skill threshold** → if the request has been repeated `SkillCreationThreshold` times → propose creating a skill (with confirmation)
6. **Improvement threshold** → if `success_rate < SkillImprovementThreshold` → propose updating the skill
7. **Memory saving** → facts about the user, entities, preferences
8. **Reflection** → at the end of a session or every N commands

### Principles

- **Never stop learning** — every session enriches memory or skills
- **Explicit improvement loop** — the agent itself proposes fixes
- **Transparent** — the user sees all creations/improvements
- **Human-in-the-loop** — skills are created only after confirmation
- **Versioned** — old skill versions are not deleted (`skill.{id}.v{N}.md`)

---

## 🧪 Acceptance Criteria Check

| Criterion                        | How to verify                                              |
| -------------------------------- | ---------------------------------------------------------- |
| A skill is created automatically | Repeat the same query 3 times → the agent proposes a skill |
| A skill is used                  | After creation — the query goes through `skill: ...`       |
| A skill improves                 | After a series of bad responses → a proposal to update     |
| Profile is saved                 | Restart → `/memory show` remembers facts                   |
| Context is carried over          | Session 1: fact → Session 2: agent remembers               |
| Reflection runs                  | After `/exit` — Reflection Engine output                   |

---

## 📦 Dependencies (NuGet)

- `Microsoft.Extensions.AI` + `Microsoft.Extensions.AI.OpenAI` — AI abstractions
- `OpenAI` — OpenAI-compatible SDK (YandexGPT, Ollama)
- `Microsoft.Data.Sqlite` — SQLite
- `YamlDotNet` — metadata parsing
- `Spectre.Console` — improved CLI
- `Telegram.Bot` — Telegram interface
- `Microsoft.Extensions.Hosting` / `Configuration.Json` — DI and configuration

---

## 📚 Documentation

| Document                                       | Description                     |
| ---------------------------------------------- | ------------------------------- |
| [docs/QUICKSTART.md](docs/QUICKSTART.md)       | Quick start in a few minutes    |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)   | Core and interface architecture |
| [docs/CONFIGURATION.md](docs/CONFIGURATION.md) | Full settings reference         |
| [docs/API.md](docs/API.md)                     | REST Web API reference          |
| [docs/BRANDING.md](docs/BRANDING.md)           | Logo, palette, brand rules      |
| [CONTRIBUTING.md](CONTRIBUTING.md)             | How to contribute               |
| [CHANGELOG.md](CHANGELOG.md)                   | Change history                  |
| [SECURITY.md](SECURITY.md)                     | Security policy                 |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)       | Code of conduct                 |

---

## 🤝 Contributing

PRs and Issues are welcome! Before starting, please read [CONTRIBUTING.md](CONTRIBUTING.md)
and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Report vulnerabilities via [SECURITY.md](SECURITY.md).

---

## 📝 License

[MIT](LICENSE) © 2026 Victor Buzin.
