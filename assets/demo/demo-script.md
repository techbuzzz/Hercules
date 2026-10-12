# Hercules 60–90 Second Demo Script

**Total target length:** 75 seconds  
**Resolution:** 1280×720 (or 1920×1080 scaled to 720 for GIF)  
**Voice:** Microsoft Edge TTS `en-US-GuyNeural` (clear, neutral)  

---

## Scene 1 — Hook (0–8 sec)

**Screen:** Terminal with repo already cloned.  
**Narration:**

> Meet Hercules — a self-improving AI agent written in C# and .NET 10.

**Action:**

1. Type in terminal:

   ```bash
   dotnet run --project src/agent/Hercules
   ```

2. Show the Hercules CLI banner loading.

---

## Scene 2 — CLI REPL (8–25 sec)

**Narration:**

> It runs as a REPL. Ask a question, and Hercules answers with context-aware routing.

**Action:**

1. Type in REPL:

   ```text
   what can you do?
   ```

2. Wait for answer that mentions skills, memory, and tools.

3. Type:

   ```text
   /skills
   ```

4. Show the skills table.

---

## Scene 3 — Skill creation (25–40 sec)

**Narration:**

> Repeat a task, and Hercules proposes a skill — then improves it automatically.

**Action:**

1. Type three times:

   ```text
   convert 30 celsius to fahrenheit
   ```

   (Accept the skill creation prompt each time if needed.)

2. Show the skill file created in `data/Skills/`.

---

## Scene 4 — Telegram bot (40–58 sec)

**Narration:**

> The same agent powers a Telegram bot, a Web API, and a React frontend.

**Action:**

1. Stop CLI with `/exit`.
2. Run:

   ```bash
   dotnet run --project src/agent/Hercules -- --telegram
   ```

3. Switch to Telegram window.
4. Send `/start` and then:

   ```text
   hello hercules
   ```

5. Show the bot reply.

---

## Scene 5 — Web UI + CTA (58–75 sec)

**Narration:**

> Or plug it into your own apps via the REST API and Astro dashboard. Star the repo, try the bot, and start building.

**Action:**

1. Show browser on `http://localhost:4321` with the Astro chat UI.
2. Send a message and show the response.
3. Fade to end card:

   - GitHub: `github.com/techbuzzz/Hercules`
   - Telegram: `t.me/HerculesAgentBot`
   - Docs: `techbuzzz.github.io/Hercules`

---

## Voice-over full text (for TTS)

```text
Meet Hercules, a self-improving AI agent written in C-sharp and dot-net ten.
It runs as a REPL. Ask a question, and Hercules answers with context-aware routing.
Repeat a task, and Hercules proposes a skill, then improves it automatically.
The same agent powers a Telegram bot, a Web API, and a React frontend.
Or plug it into your own apps via the REST API and Astro dashboard.
Star the repo, try the bot, and start building.
```

**Note for TTS:** C# → "C-sharp", .NET → "dot-net".
