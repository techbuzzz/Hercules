# 04 — CLI planner

Turn Hercules into a personal planner you control from the terminal.

## Run

```bash
dotnet run --project src/agent/Hercules
```

Then try:

```text
> remind me to deploy the web api at 3pm
> list my tasks for today
> mark deploy the web api as done
> /reflect
```

## What happens

- The planner skill parses dates and priorities.
- Tasks are stored in `data/Memory/planner.md`.
- Hercules learns your productivity patterns and suggests improvements after `/reflect`.

## Files

- `skill.md` — reusable planner skill
- `README.md` — this file
