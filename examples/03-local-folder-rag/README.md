# 03 — RAG over a local folder

Index a folder of text files on your disk and ask questions over them.

## What you need

- A folder with `.md`, `.txt`, or `.pdf` files
- An embedding-capable model (YandexGPT embeddings or a local model)

## Run

1. Put documents into `data/Documents/` (or any path you set).
2. Start the agent:

   ```bash
   dotnet run --project src/agent/Hercules
   ```

3. Ask:

   ```text
   index my documents folder and answer: what is the skill router?
   ```

## What happens

- The agent scans the folder.
- It chunks text and stores lightweight embeddings.
- Relevant chunks are injected into the prompt, then the LLM answers from the sources.

## Files

- `skill.md` — reusable skill that handles `index` and `ask` intents
- `README.md` — this file
