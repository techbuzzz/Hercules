# E2E secrets — NOT tracked by git

This directory holds the Ollama Cloud API key used by the isolated test stack.

## Why a Docker secret

The key is bind-mounted **read-only** into the container at
`/run/secrets/ollama_cloud_key`. `deploy/e2e/entrypoint.sh` reads it and exports
`HERCULES_LLM__OLLAMACLOUD__APIKEY` immediately before starting the agent.

That matters because `docker inspect <container>` prints the entire environment.
A key passed as an ordinary env var or written into `docker-compose.yml` would
be visible there, in the file, and at risk of being committed. A secret stays out
of all three.

## Setup (one-time)

Create the key file. It is gitignored, so this is safe to do in a checkout:

```powershell
New-Item -Path deploy\e2e\secrets\ollama_cloud.key -Value "your-ollama-cloud-key" -Force
```

A trailing newline is fine — `entrypoint.sh` strips it, because providers send the
raw token as a bearer credential.

## Apply

The key is read at container **start**, so the container must be recreated to
pick up a new or changed key:

```powershell
docker compose -f deploy\e2e\docker-compose.yml up -d --force-recreate
```

## Verify

```powershell
# Key must NOT appear in inspect output:
docker inspect hercules-e2e-agent --format '{{range .Config.Env}}{{println .}}{{end}}' | Select-String APIKEY

# It SHOULD be logged (length only) in the container output:
docker logs hercules-e2e-agent 2>&1 | Select-String "Ollama Cloud key"
```

## Optional: pick a different model

`gpt-oss:120b` is the default and is large and slow. Override per-shell without
editing any tracked file:

```powershell
$env:HERCULES_LLM__OLLAMACLOUD__MODEL = "qwen3-coder:480b"
docker compose -f deploy\e2e\docker-compose.yml up -d --force-recreate
```

The **LLM** view inside Studio lists what the provider reports as available.