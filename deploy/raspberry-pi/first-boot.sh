#!/bin/bash
# =============================================================================
# Hercules — First-Boot Provisioning Script (Raspberry Pi)
# Target: Raspberry Pi OS (Bookworm, 64-bit)
# Must run as root. Called by cloud-init or rc.local on first boot.
#
# Responsibilities:
#   1. Expand filesystem (raspi-config auto-handles this, but we guard)
#   2. Provision Wi-Fi (wpa_supplicant) if Wi-Fi credentials provided
#   3. Initialise data directories
#   4. Generate device identity / enrollment token
#   5. Write provision.env consumed by docker-compose
#   6. Pull / start the Hercules container
#
# Safety: Idempotent — safe to re-run.
# =============================================================================

set -euo pipefail
IFS=$'\n'

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly DATA_ROOT="/data/hercules"
readonly PROVISION_ENV="${SCRIPT_DIR}/provision.env"
readonly DOCKER_COMPOSE="${SCRIPT_DIR}/docker-compose.yml"

# Logging
log()  { echo "[$(date -Iseconds)] [provision] $*"; }
warn() { echo "[$(date -Iseconds)] [provision] WARN: $*" >&2; }
die()  { echo "[$(date -Iseconds)] [provision] ERROR: $*" >&2; exit 1; }

# ── Guards ────────────────────────────────────────────────────────────────────
[[ $EUID -eq 0 ]] || die "Must run as root"

# ── Config ────────────────────────────────────────────────────────────────────
# Wi-Fi provisioning (optional)
WIFI_SSID="${WIFI_SSID:-}"
WIFI_PSK="${WIFI_PSK:-}"
WIFI_COUNTRY="${WIFI_COUNTRY:-GB}"

# Enrollment
ENROLLMENT_URL="${ENROLLMENT_URL:-}"
ENROLLMENT_TOKEN="${ENROLLMENT_TOKEN:-}"

# LLM credentials (can be set here or via environment)
LLM_PROVIDER="${LLM_PROVIDER:-yandexgpt}"
YANDEX_FOLDER_ID="${YANDEX_FOLDER_ID:-}"
YANDEX_API_KEY="${YANDEX_API_KEY:-}"

# Security
AUTO_ROTATE_IDENTITY="${AUTO_ROTATE_IDENTITY:-true}"
REQUIRE_PACKAGE_SIGNATURE="${REQUIRE_PACKAGE_SIGNATURE:-false}"

# ── Step 1: Filesystem expansion ──────────────────────────────────────────────
log "Step 1: Checking filesystem"
if [[ -f /usr/bin/raspi-config ]]; then
    log "raspi-config found — filesystem expansion handled by raspi-config"
else
    log "No raspi-config — assuming filesystem already expanded"
fi

# ── Step 2: Wi-Fi provisioning ─────────────────────────────────────────────────
provision_wifi() {
    if [[ -z "$WIFI_SSID" ]]; then
        log "Wi-Fi SSID not set — skipping Wi-Fi provisioning"
        return 0
    fi

    log "Step 2: Provisioning Wi-Fi SSID='$WIFI_SSID'"

    local wpa_conf="/etc/wpa_supplicant/wpa_supplicant.conf"
    if [[ ! -f "$wpa_conf" ]]; then
        wpa_conf="/etc/wpa_supplicant/wpa_supplicant-wlan0.conf"
    fi

    [[ -f "$wpa_conf" ]] || die "wpa_supplicant.conf not found"

    # Backup existing
    cp "$wpa_conf" "${wpa_conf}.bak.$(date +%Y%m%d%H%M%S)"

    # Remove existing network blocks (prevents duplicates on re-run)
    # Insert country + network block before the last '}'
    local tmp
    tmp=$(mktemp)
    grep -v '^[[:space:]]*network=' "$wpa_conf" > "$tmp"
    cat >> "$tmp" <<EOF
country=$WIFI_COUNTRY
network={
    ssid="$WIFI_SSID"
    psk="$WIFI_PSK"
    key_mgmt=WPA-PSK
}
EOF
    mv "$tmp" "$wpa_conf"
    chmod 600 "$wpa_conf"

    # Restart Wi-Fi
    if command -v wpa_cli > /dev/null 2>&1; then
        wpa_cli -i wlan0 reconfigure || warn "wpa_cli reconfigure failed"
    fi

    log "Wi-Fi provisioned — restart will connect to '$WIFI_SSID'"
}

provision_wifi

# ── Step 3: Data directory initialisation ─────────────────────────────────────
log "Step 3: Initialising data directories at $DATA_ROOT"
mkdir -p "$DATA_ROOT"/{security/identity,security/certs,skills,memory,logs}
chmod 700 "$DATA_ROOT/security/identity"
chmod 700 "$DATA_ROOT/security/certs"
chmod 755 "$DATA_ROOT"

# ── Step 4: Device identity token ─────────────────────────────────────────────
log "Step 4: Generating device enrollment token"
ENROLLMENT_TOKEN="${ENROLLMENT_TOKEN:-$(openssl rand -hex 32)}"

# ── Step 5: Write provision.env ──────────────────────────────────────────────
log "Step 5: Writing provision.env"
cat > "$PROVISION_ENV" <<EOF
# ──────────────────────────────────────────────────────────────────────────────
# Hercules Edge Provisioning — DO NOT COMMIT THIS FILE
# Copy from provision.env.template and fill credentials
# ──────────────────────────────────────────────────────────────────────────────

# ── LLM Configuration ────────────────────────────────────────────────────────
HERCULES_LLM__PROVIDER=${LLM_PROVIDER}
HERCULES_LLM__YANDEXGPT__FOLDERID=${YANDEX_FOLDER_ID}
HERCULES_LLM__YANDEXGPT__APIKEY=${YANDEX_API_KEY}
HERCULES_LLM__OLLAMA_LOCAL__ENDPOINT=http://localhost:11434/v1
HERCULES_LLM__OLLAMA_LOCAL__MODEL=llama3.2

# ── Enrollment ────────────────────────────────────────────────────────────────
HERCULES_EDGE__ENROLLMENT_URL=${ENROLLMENT_URL}
HERCULES_EDGE__ENROLLMENT_TOKEN=${ENROLLMENT_TOKEN}
HERCULES_EDGE__DEVICE_ID=$(cat /sys/class/net/eth0/address 2>/dev/null || cat /proc/cpuinfo | grep Serial | awk '{print $3}' | tr '[:upper:]' '[:lower:]')
HERCULES_EDGE__ENROLLED=false

# ── Security ──────────────────────────────────────────────────────────────────
HERCULES_SECURITY_OPS__ENABLED=true
HERCULES_SECURITY_OPS__AUTO_ROTATE_IDENTITY=${AUTO_ROTATE_IDENTITY}
HERCULES_SECURITY_OPS__REQUIRE_PACKAGE_SIGNATURE=${REQUIRE_PACKAGE_SIGNATURE}
HERCULES_SECURITY_OPS__IDENTITY_STORAGE_PATH=/data/security/identity
HERCULES_SECURITY_OPS__CERTIFICATE_STORAGE_PATH=/data/security/certs

# ── Storage ───────────────────────────────────────────────────────────────────
HERCULES_STORAGE__DATAROOT=/data
HERCULES_STORAGE__SKILLSDIR=/data/skills
HERCULES_STORAGE__MEMORYDIR=/data/memory
HERCULES_STORAGE__SQLITEFILE=/data/sessions.db

# ── Mesh ─────────────────────────────────────────────────────────────────────
HERCULES_MESH__ENABLED=true
HERCULES_MESH__DISCOVERY__ENABLEMDNS=false
EOF

chmod 600 "$PROVISION_ENV"
log "provision.env written"

# ── Step 6: Pull and start container ─────────────────────────────────────────
log "Step 6: Starting Hercules container"

if command -v docker > /dev/null 2>&1; then
    cd "$SCRIPT_DIR"
    docker compose pull --quiet || warn "docker compose pull failed (offline?)"
    docker compose up -d --wait || die "docker compose up failed"
    log "Container started"
else
    warn "Docker not found — skipping container start"
    warn "Run 'docker compose up -d' manually once Docker is available"
fi

log "First-boot provisioning complete"
log "Enrolment token (save securely): $ENROLLMENT_TOKEN"
