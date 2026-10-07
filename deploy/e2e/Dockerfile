# =============================================================================
# Hercules — isolated E2E/UI-test image
#
# Mirrors the web-first runtime topology of ADR-0009: ONE process serves the API
# and the Studio SPA at /ui. That is the only arrangement in which a browser test
# exercises what users actually get, so this image is deliberately not a
# multi-container split.
#
# Stage 1  node       → builds the Studio SPA (Vue 3 + Vite)
# Stage 2  dotnet/sdk → publishes Hercules.WebApi
# Stage 3  dotnet/aspnet → runtime: agent + prebuilt Studio bundle
# =============================================================================

# ── Stage 1: Studio SPA ────────────────────────────────────────────────────────
FROM node:22-alpine AS studio

WORKDIR /studio

# Manifests first so `npm ci` is cached independently of source edits.
COPY src/hercules-studio/package.json src/hercules-studio/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/hercules-studio/ ./

# vite.config.ts sets base "/ui/" and outDir "../dist" — the agent mounts the
# bundle there, so the base path is load-bearing, not cosmetic.
RUN npm run build

# ── Stage 2: agent ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS agent

WORKDIR /src

# global.json pins the SDK (R40); Directory.Build.props pins Version/analyzers.
# Both must be present before restore or the pinned version is not honoured.
COPY global.json Directory.Build.props Directory.Build.targets ./
COPY src/agent/Hercules.slnx ./src/agent/
COPY src/agent/Hercules.csproj ./src/agent/
COPY src/agent/Hercules.WebApi/Hercules.WebApi.csproj ./src/agent/Hercules.WebApi/
COPY src/agent/SkillSdk/Hercules.SkillSdk.csproj ./src/agent/SkillSdk/
COPY src/workflow-server/Hercules.WorkflowServer.csproj ./src/workflow-server/

RUN dotnet restore src/agent/Hercules.WebApi/Hercules.WebApi.csproj \
 && dotnet restore src/workflow-server/Hercules.WorkflowServer.csproj

COPY src/agent/ ./src/agent/
COPY src/workflow-server/ ./src/workflow-server/

# openapi.json is a build-time artefact that must not be regenerated implicitly
# (R11): generation is opt-in, and CI fails on drift. Plain publish only.
RUN dotnet publish src/agent/Hercules.WebApi/Hercules.WebApi.csproj \
        --configuration Release \
        --output /publish \
        --no-restore \
    /p:OpenApiGenerateDocumentOnBuild=false

# The workflow server is a SECOND service (ADR-0008, port 8430). It publishes to
# its own directory on purpose: both projects ship an appsettings.json, and a
# shared output folder would hit the same NETSDK1152 collision the WebApi did.
RUN dotnet publish src/workflow-server/Hercules.WorkflowServer.csproj \
        --configuration Release \
        --output /publish-wf \
        --no-restore

# ── Stage 3: runtime ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://+:8421 \
    ASPNETCORE_ENVIRONMENT=Development \
    HERCULES_DATA_ROOT=/data

WORKDIR /app

# curl is not in the aspnet runtime image; the HEALTHCHECK below needs it. Kept in
# this image only (not the edge one) because a browser test needs a trustworthy
# readiness signal before it starts driving the UI.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=agent /publish ./
COPY --from=agent /publish-wf /app-wf

# Reads the Ollama Cloud key from a Docker secret and exports it, so the key never
# appears in the image, the compose file, or `docker inspect`. See entrypoint.sh.
COPY deploy/e2e/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Studio bundle. ResolveStudioUiProvider honours an absolute WebApi:StudioUiPath,
# so /app/ui is mounted at /ui regardless of the repo layout inside the image.
COPY --from=studio /studio/dist /app/ui

# Non-root: the agent writes skills, memory and keys.json.
# UID/GID 10001, not 1000 — the aspnet base image already ships an `ubuntu`
# account on 1000, and groupadd -g 1000 exits 4 on a duplicate GID.
RUN groupadd -g 10001 hercules \
 && useradd -u 10001 -g hercules -s /sbin/nologin hercules \
 && mkdir -p /data/security /data/skills /data/memory /data-wf \
 && chown -R hercules:hercules /data /data-wf /app /app-wf

USER hercules

EXPOSE 8421

# /api/health is exempt from the X-Api-Key gate (ApiKeyMiddleware), so the probe
# needs no credentials.
HEALTHCHECK --interval=10s --timeout=5s --start-period=45s --retries=6 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8421/api/health || exit 1"]

ENTRYPOINT ["/app/entrypoint.sh"]