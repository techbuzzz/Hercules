# 02 — GitHub PR summary

Ask Hercules to summarize open pull requests from any public GitHub repository.

## Run

1. Start CLI:

   ```bash
   dotnet run --project src/agent/Hercules
   ```

2. Ask:

   ```text
   summarize open PRs in github.com/techbuzzz/Hercules
   ```

## What happens

- Hercules calls `https://api.github.com/repos/{owner}/{repo}/pulls` via the `http` tool.
- It receives the JSON list of PRs.
- The agent extracts title, author, and body, then returns a 3-bullet summary per PR.

## Optional: make it a skill

Drop `skill.md` into `data/Skills/` and the agent will reuse it every time you mention "PR" or "pull request".

## Files

- `skill.md` — reusable skill
- `README.md` — this file
