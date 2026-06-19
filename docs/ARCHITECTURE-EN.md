# Architecture

Hercules consists of a reusable **agent core** and three **façade interfaces**
(CLI, Telegram, Web API + frontend) that contain no business logic of their own.

## Component Overview

```
                  ┌─────────────┐   ┌──────────────┐   ┌──────────────────────┐
    User          │   CLI (REPL)│   │ Telegram bot │   │ Web API + Astro UI   │
                  └──────┬──────┘   └──────┬───────┘   └──────────┬───────────┘
                         │                 │                      │
                         └─────────────────┼──────────────────────┘
                                           ▼
                                  ┌──────────────────┐
                                  │     AgentCore    │  main processing loop
                                  └───┬───────┬──────┘
               ┌──────────────────────┘       └──────────────────────┐
               ▼            ▼            ▼            ▼               ▼
         SkillRouter  SkillManager  MemoryManager  ReflectionEngine  ILLMClient
               │            │            │              │             │
               └──── FileSkillRepository / MemoryStore / SqliteSessionStore ──── ResilientLLMClient
                                                                                   │
                                                                  ┌────────────────┼────────────────┐
                                                               YandexGPT      Ollama Cloud     Ollama Local
                                                                  (via Microsoft.Extensions.AI / OpenAI)
```

## Layers

### 1. Interfaces (`CLI/`, `Telegram/`, `Hercules.WebApi/`, `hercules-web/`)
Accept user input, call `AgentCore.HandleAsync()`, and display the result.
The Web API uses `Agent/WebApiAdapter.cs` (DTO + mapping) as a thin layer over the core.

### 2. Agent Core (`Agent/`)
- **`AgentCore`** — orchestration: load context → route → call LLM → log →
  check skill creation/improvement thresholds → save memory.
- **`SkillRouter`** — selects a skill by triggers or returns a direct LLM answer.
- **`SkillManager`** — skill CRUD, versioning (`skill.{id}.v{N}.md`), LLM generation and improvement.
- **`MemoryManager`** — user model, preferences, entities, context carry-over between sessions.
- **`ReflectionEngine`** — self-analysis after a session or every N commands, reflection reports.

### 3. LLM Layer (`LLM/`)
Unified `ILLMClient` interface on top of `Microsoft.Extensions.AI` (`IChatClient`).
- `YandexGPTClient`, `LocalLLMClient` (Ollama/LM Studio) — concrete providers.
- `LlmClientFactory` — selects a client by provider name.
- `ResilientLLMClient` — resilience and fallback chain from `Llm:Fallback`.

### 4. Storage (`Storage/`)
Hybrid:
- **Files** (`FileSkillRepository`, `MemoryStore`) — Markdown/JSON for skills and memory (transparent knowledge).
- **SQLite** (`SqliteSessionStore`) — sessions, interactions, metrics, repeat counters.

## Request Processing Flow (self-improving cycle)
1. **Request** → load profile and context from memory.
2. **Routing** → `SkillRouter` looks up a skill by triggers.
3. **LLM response** → with a skill (skill-prompt) or directly (direct).
4. **Logging** → input/output/confidence/mode in SQLite.
5. **Creation threshold** → repeat `SkillCreationThreshold` times → propose creating a skill (with confirmation).
6. **Improvement threshold** → `success_rate < SkillImprovementThreshold` → propose updating the skill.
7. **Memory** → extract facts about the user, entities, preferences.
8. **Reflection** → at the end of a session or every N commands.

## Design Principles
- **Never stop learning** — every session enriches memory or skills.
- **Human-in-the-loop** — skills are created/changed only after confirmation (in CLI).
- **Transparent & versioned** — old skill versions are preserved; knowledge is readable as Markdown.
- **Provider-agnostic** — switch LLM without changing core logic.

## Runtime Data (`data/`)
```
data/
├── Skills/   # skill.{id}.md / .prompt.md / .meta.json / .usage.json / .v{N}.md
├── Memory/   # user_profile.md, preferences.md, entities.md, context_{date}.md
└── sessions.db  # SQLite: sessions, interactions, metrics
```
