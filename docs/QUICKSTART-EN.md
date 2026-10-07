# Quick Start

This guide will help you run **Hercules** locally in just a few minutes.

## 1. Requirements
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — `dotnet --version` should show `10.x`
- [Node.js 20+](https://nodejs.org/) — only if you need the web interface
- Access to at least one LLM provider:
  - **YandexGPT** (key + Yandex Cloud folderId), **or**
  - **Ollama Cloud** (API key), **or**
  - **Ollama Local** (`ollama serve` on `localhost:11434`)

## 2. Clone and Build
```bash
git clone https://github.com/<owner>/hercules.git
cd hercules
dotnet restore
dotnet build Hercules.slnx
```

## 3. Configure the Provider
Open `appsettings.json` and set the active provider and keys. Example for local Ollama:
```jsonc
{
  "Llm": {
    "Provider": "ollama-local",
    "OllamaLocal": { "Endpoint": "http://localhost:11434/v1", "Model": "llama3.1" }
  }
}
```
It's best to pass secrets via environment variables:
```bash
export HERCULES_Llm__Provider=yandexgpt
export HERCULES_Llm__YandexGpt__ApiKey=*** 
export HERCULES_Llm__YandexGpt__FolderId=***
```

## 4. Run the CLI (primary mode)
```bash
dotnet run --project Hercules
```
Enter a query in the REPL. Repeat the same query 3 times — the agent will propose creating a skill.

## 5. Run the Web API with Studio (optional)
```bash
# Single process — the agent serves the Studio SPA at /ui (ADR-0009)
dotnet run --project Hercules.WebApi
```
Open `http://localhost:8421/ui/`.

For UI work with hot reload, run the Studio dev server alongside it:

```bash
# Terminal 2 — Studio dev server (port :4330, base path /ui/)
cd src/hercules-studio
npm install
npm run dev
```
Open `http://localhost:4330/ui/`.

There is no separate frontend process and no Electron shell. The old Astro frontend
(`src/hercules-web`) is deprecated — see `src/hercules-web/DEPRECATED.md`.

## 6. Run the Telegram Bot (optional)
```bash
export HERCULES_Telegram__Enabled=true
export HERCULES_Telegram__BotToken=<token from @BotFather>
dotnet run --project Hercules -- --telegram
```

## What's Next
- [Architecture](ARCHITECTURE-EN.md) — how the agent is structured
- [Configuration](CONFIGURATION-EN.md) — all parameters
- [API](API-EN.md) — REST endpoint reference
