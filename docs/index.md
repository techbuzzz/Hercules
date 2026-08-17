---
layout: default
title: Hercules — Self-improving AI agent on C# / .NET 10
---

# Hercules

**Self-improving AI agent on C# / .NET 10**

Creates skills from experience · improves them during use · remembers context between sessions · connects agents into a mesh.

[![GitHub Stars](https://img.shields.io/github/stars/techbuzzz/Hercules?style=social)](https://github.com/techbuzzz/Hercules/stargazers)
[![Build](https://img.shields.io/github/actions/workflow/status/techbuzzz/Hercules/build.yml?branch=main&logo=github&label=build)](https://github.com/techbuzzz/Hercules/actions)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-15-239120?logo=csharp&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-yellow.svg)

---

## One-liner

Hercules is a runnable, self-improving C# micro-agent that learns from conversations, versions its own skills, runs code in a sandbox, and can team up with other agents over a mesh.

---

## Watch the demo

[![Demo cover](../assets/demo/demo-video-cover.svg)](demo)

[🎬 Watch the 60-sec demo](demo) · [📁 Examples](../examples) · [🤝 Good first issues](https://github.com/techbuzzz/Hercules/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)

---

## Quick start

```bash
# Clone
git clone https://github.com/techbuzzz/Hercules.git && cd Hercules

# Run CLI REPL
dotnet run --project src/agent/Hercules

# Run Web API + Astro frontend
dotnet run --project src/agent/Hercules.WebApi
cd src/hercules-web && npm install && npm run dev
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  Interfaces: CLI REPL · Telegram bot · Web API · Astro SPA          │
├─────────────────────────────────────────────────────────────────────┤
│  Agent Core   →  Skill Router  →  Reflection  →  Memory Manager   │
│        ↓              ↓                ↓              ↓             │
│   Multi-role LLM routing (YandexGPT / Ollama / LM Studio)             │
├─────────────────────────────────────────────────────────────────────┤
│  Tools: HTTP · execute_code · A2A · MCP · WASM                      │
├─────────────────────────────────────────────────────────────────────┤
│  Mesh: capability registry · intent routing · circuit breaker       │
├─────────────────────────────────────────────────────────────────────┤
│  Storage: Markdown skills/memory + SQLite logs + Redis/NATS/PG      │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Hercules vs Hermes vs AutoGen

| Capability | Hercules | Hermes (Nous) | AutoGen |
| --- | --- | --- | --- |
| Self-improving skills | ✅ built-in | research concept | manual |
| C# / .NET first-class | ✅ | ❌ | Python |
| Sandboxed code execution | ✅ 3-layer | ❌ | partial |
| Multi-agent mesh | ✅ | ❌ | ✅ |
| YandexGPT / Ollama out of box | ✅ | ❌ | via adapters |
| Long-term memory | ✅ layered | ❌ | optional |
| Telegram bot included | ✅ | ❌ | build yourself |

---

## Examples

- [Daily Telegram report](../examples/01-telegram-daily-report)
- [GitHub PR summary](../examples/02-github-pr-summary)
- [RAG over local folder](../examples/03-local-folder-rag)
- [CLI planner](../examples/04-cli-planner)
- [Web API + React](../examples/05-web-api-react)

---

## Documentation

- [Quick start](QUICKSTART-EN.md)
- [Architecture](ARCHITECTURE-EN.md)
- [Agent mesh](AGENT-MESH-EN.md)
- [Configuration](CONFIGURATION-EN.md)
- [API reference](API-EN.md)

---

## CTA

- ⭐ [Star on GitHub](https://github.com/techbuzzz/Hercules)
- 🤖 [Try the Telegram bot](https://t.me/HerculesAgentBot)
- 🛠️ [Pick a good first issue](https://github.com/techbuzzz/Hercules/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
