# 01 — Daily report in Telegram

Get a personal daily digest every morning: weather, calendar highlights, and a short task list pulled from memory.

## What you need

- A Telegram bot token from [@BotFather](https://t.me/botfather)
- YandexGPT, Ollama Cloud, or a local model running

## Run

1. Copy the skill file to the runtime data folder:

   ```bash
   mkdir -p data/Skills
   cp skill.md data/Skills/skill.daily-report.md
   ```

2. Enable Telegram in `appsettings.json`:

   ```json
   "Telegram": {
     "Enabled": true,
     "BotToken": "YOUR_BOT_TOKEN"
   }
   ```

3. Start the bot:

   ```bash
   dotnet run --project src/agent/Hercules -- --telegram
   ```

4. In Telegram send `/start`, then type:

   ```text
   send me a daily report at 09:00
   ```

## What happens

- Hercules recognizes the `daily-report` intent from the skill.
- It stores your preference in `data/Memory/preferences.md`.
- On the next run (or via a cron trigger) it builds a Markdown report and sends it to you.

## Files

- `skill.md` — skill definition
- `appsettings.fragment.json` — config snippet
- `README.md` — this file
