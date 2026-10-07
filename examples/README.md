# Hercules Examples

Ready-to-run scenarios that show what Hercules can do in a few commands.

| # | Scenario | What you'll learn |
| - | -------- | ----------------- |
| `01-telegram-daily-report` | Daily report in Telegram | Run the bot, schedule a skill, get a daily digest |
| `02-github-pr-summary` | PR summary from GitHub | Use the `http` tool to call GitHub API and summarize PRs |
| `03-local-folder-rag` | RAG over a local folder | Index Markdown/PDF files and ask questions over them |
| `04-cli-planner` | CLI planner | Build a personal todo/schedule skill in REPL |
| `05-web-api-react` | Web API + React face | Embed Hercules Web API into a React app |

Each folder contains:

- `README.md` — what it does and how to run it
- `skill.md` (when applicable) — a ready-to-drop skill file for `data/Skills/`
- `appsettings.json` fragment — copy-paste config
- `demo.gif` placeholder — replace with your own screen recording

## Quick start any example

```bash
# from repo root
dotnet run --project src/agent/Hercules
# then follow the README in the example folder
```

Want to add your own? See [CONTRIBUTING-EN.md](../CONTRIBUTING-EN.md).
