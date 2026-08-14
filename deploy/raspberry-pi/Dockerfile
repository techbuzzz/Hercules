# =============================================================================
# Hercules — Raspberry Pi ARM64 Docker image
# Multi-stage build: build → runtime
# Target: Raspberry Pi 4/5 (aarch64 / ARM64)
# =============================================================================

# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

# Build-time ARG (mirrored to runtime arch)
ARG TARGETARCH
ARG BUILDARCH
ARG BUILDPLATFORM

# Install build deps
RUN apk add --no-cache \
        linux-headers \
        musl-dev \
        clang \
        llvm \
        lld \
        wasmtime \
        wabt

WORKDIR /src

# Copy solution + project files (layer cache optimisation)
COPY Hercules.sln ./
COPY src/agent/Hercules.csproj src/agent/Hercules.csproj
COPY src/agent/Hercules.WebApi/Hercules.WebApi.csproj src/agent/Hercules.WebApi/Hercules.WebApi.csproj
COPY tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj

# Restore packages
RUN dotnet restore src/agent/Hercules.csproj

# Copy source
COPY src/ src/

# Build for target architecture
# Note: cross-compilation to aarch64 requires self-contained publish (not native cross-build)
# For Raspberry Pi: publish as self-contained to avoid needing .NET runtime on the Pi
RUN dotnet publish src/agent/Hercules.csproj \
        --configuration Release \
        --output /publish \
        --no-restore \
        --self-contained true \
        --runtime linux-arm64 \
        /p:PUBLISH_READY_TO_RUN=true

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM --platform=linux/arm64 alpine:3.21 AS runtime

# Install runtime dependencies
RUN apk add --no-cache \
        icu-data-full \
        ca-certificates \
        curl \
        wabt \
        wasmtime

# Hercules data directory (persistent via Docker volume)
ENV HERCULES_DATA=/data
ENV HERCULES_STORAGE_DATAROOT=/data
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Non-root user for security
RUN addgroup -g 1000 hercules && \
    adduser -u 1000 -G hercules -s /bin/sh -D hercules

WORKDIR /app

# Copy published binary
COPY --from=build /publish .

# Pre-create data directories with correct ownership
RUN mkdir -p ${HERCULES_DATA}/security/identity \
               ${HERCULES_DATA}/security/certs \
               ${HERCULES_DATA}/skills \
               ${HERCULES_DATA}/memory \
               && chown -R hercules:hercules ${HERCULES_DATA}

# Copy default appsettings (will be overridden by provision.env via envsubst)
COPY deploy/raspberry-pi/appsettings.edge.json /app/appsettings.edge.json

USER hercules

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

EXPOSE 8080

ENTRYPOINT ["/app/Hercules"]
