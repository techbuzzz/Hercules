#!/bin/sh
# =============================================================================
# Hercules E2E container entrypoint.
#
# Purpose: get the LLM API key out of a Docker secret and into the process
# environment, WITHOUT it ever appearing in `docker inspect`, the compose file,
# or git.
#
# Why not just an env var: `docker inspect <container>` prints the whole
# environment, and `.env` / compose files are easy to commit by accident. A
# compose secret is a file bind-mounted read-only at /run/secrets/<name>, so the
# key stays out of both the image layers and the container metadata.
#
# The agent reads its provider config from ordinary .NET configuration
# (HERCULES_* env vars are added by Program.cs), so exporting here is enough —
# no application code needs to know secrets exist.
# =============================================================================
set -eu

SECRET_DIR="/run/secrets"
OLLAMA_SECRET="${SECRET_DIR}/ollama_cloud_key"

# Strip a trailing newline. Secret files conventionally end in one, and an
# env-var-carried key must not — providers send the raw token as a bearer.
read_secret() {
    _file="$1"
    if [ -r "$_file" ]; then
        tr -d '\r\n' < "$_file"
    else
        echo ""
    fi
}

if [ -f "$OLLAMA_SECRET" ]; then
    _key="$(read_secret "$OLLAMA_SECRET")"
    # The tracked placeholder keeps `docker compose up` working before a real key
    # is added. Sending it would produce an opaque 401 from ollama.com, so refuse
    # it explicitly and say what to do instead.
    case "$_key" in
        ""|REPLACE_WITH_YOUR_OLLAMA_CLOUD_KEY|CHANGE_ME|your-key|REPLACE_ME)
            echo "[entrypoint] ollama_cloud_key still contains the placeholder."
            echo "[entrypoint] Replace deploy/e2e/secrets/ollama_cloud_key with your real"
            echo "[entrypoint] key, then: docker compose -f deploy/e2e/docker-compose.yml up -d --force-recreate"
            echo "[entrypoint] LLM calls will fail with 'all providers unavailable' until then."
            ;;
        *)
            export HERCULES_LLM__OLLAMACLOUD__APIKEY="$_key"
            echo "[entrypoint] Ollama Cloud key loaded from Docker secret (${#_key} chars)."
            ;;
    esac
else
    # Not fatal. The stack is still useful for testing every view that does not
    # need an LLM, and the agent degrades gracefully rather than refusing to boot.
    echo "[entrypoint] No Docker secret at ${OLLAMA_SECRET}."
    echo "[entrypoint] To enable a real LLM, create deploy/e2e/secrets/ollama_cloud.key"
    echo "[entrypoint] and run: docker compose -f deploy/e2e/docker-compose.yml up -d --force-recreate"
fi

exec dotnet /app/Hercules.WebApi.dll