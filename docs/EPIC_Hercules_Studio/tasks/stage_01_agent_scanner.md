# Stage 1 — Agent Scanner + Connection Manager + CheckIn/CheckOut

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 1-2 недели
**Dependencies (backend):** task_096 (port migration), task_097 (dual API keys), task_098 (CheckIn/CheckOut)
**Dependencies (Studio):** Stage 0

## Goal

Studio находит агентов на машине (port scan 8421-8521 + legacy 5000, Windows process scan), подключается, переключается между ними. CheckIn/CheckOut с heartbeat (60s TTL). Multiple connections management.

## Tasks

### 1.1 — AgentScanner (main process)

- [ ] `electron/main/scanner.ts`:
  - `scanPorts(start, end, concurrent, timeout)` — TCP connect probe
  - Default range: 8421-8521 + legacy 5000
  - Concurrent: 50 ports simultaneously
  - Connect timeout: 300ms
  - For each open port: `GET /agent.manifest.json` (200ms timeout)
    - 200 + valid manifest (agentId non-empty) → discovered agent
    - 401 → mark "auth required"
    - else → skip
  - `scanProcesses()` — Windows process scan:
    - `Get-Process Hercules.WebApi` (или `tasklist /fi "imagename eq Hercules.WebApi.exe"`)
    - For each process: query listening ports (`Get-NetTCPConnection -OwningProcess <pid>`)
    - Probe manifest on found ports
  - `mergeResults()` — dedup by agentId, prefer process-discovered (has PID info)
  - Progress callback → IPC → renderer (scan progress bar)
  - Cache results in SQLite `scan_cache`

### 1.2 — DiscoverAgents view

- [ ] `views/DiscoverAgents.vue`:
  - "Scan" button → triggers `scanner.scan()` via IPC
  - Progress bar during scan
  - Results list: agent name, endpoint, agentId, auth required badge
  - "Add" button per agent → pre-fills AddConnectionForm with discovered URL
  - Auto-scan on Studio startup (если setting `autoScanOnStartup` = true, default true)

### 1.3 — ConnectionManager (main process)

- [ ] `electron/main/connections.ts`:
  - `list()` — read `userData/connections.json`
  - `add(conn)` — validate (health + manifest), save API key to safeStorage, save connection to JSON, checkin
  - `remove(id)` — checkout, remove from JSON, remove key from safeStorage
  - `update(id, patch)` — update connection metadata
  - `healthCheck(id)` — `GET /api/health` + `GET /agent.manifest.json`
  - Health polling: every 30s for all connections
  - Reconnect: exponential backoff (1s → 2s → 5s → 10s cap) on health failure
  - CheckIn: `POST /api/system/checkin` on connect
  - Heartbeat: `POST /api/system/checkin/heartbeat` every 30s (TTL 60s)
  - Checkout: `POST /api/system/checkout` on disconnect/remove
  - `getSystemKey(id)` / `setSystemKey(id, key)` — safeStorage for system keys
- [ ] Connection model: `{id, name, baseUrl, agentId, displayName, authScheme, lastSeen, status, checkedOut, checkedOutBy}`

### 1.4 — Connections store (renderer)

- [ ] `stores/connections.ts` (расширить из Stage 0):
  - `connections: Connection[]`
  - `activeId: string | null`
  - `discovered: DiscoveredAgent[]`
  - `connect(id)`, `disconnect(id)`, `setActive(id)`
  - `refresh()` — health check all
  - `scan()` — trigger scanner, update discovered
  - Computed: `active` (current connection), `online` (status online), `offline`

### 1.5 — AgentList view

- [ ] `views/AgentList.vue`:
  - Table/list of all connections
  - Columns: name, endpoint, agentId, status (online/offline/degraded), checkedOut, lastSeen
  - Actions per agent: "Connect" (set active), "Edit" (rename/rekey), "Remove", "Refresh"
  - Status indicator: green (online), amber (degraded), red (offline)
  - Check-in indicator: "Checked out by me" / "Available" / "Checked out by other"
  - "Restart" button — disabled с tooltip "Requires backend task_099 + system key" (на Stage 6)

### 1.6 — AddConnectionForm (расширить из Stage 0)

- [ ] `components/connections/AddConnectionForm.vue`:
  - Поля: name, baseUrl (default http://localhost:8421), contribute apiKey, system apiKey (optional)
  - "Test connection" → health + manifest + checkin status
  - Warning если агент уже checked out
  - "Save" → add connection + checkin
  - Support "discovered agent" pre-fill (from DiscoverAgents)

### 1.7 — System key elevation

- [ ] `components/common/SystemKeyDialog.vue`:
  - Dialog для ввода system key перед system-операцией
  - "Remember for this session" option → safeStorage
  - После ввода → StatusBar показывает "system mode" индикатор
- [ ] `sdk/client.ts` — `setSystemKey(key)`, `clearSystemKey()`, `hasSystemKey()`

### 1.8 — StatusBar (расширить)

- [ ] `components/layout/StatusBar.vue`:
  - Active agent: name + status (online/offline/degraded)
  - Latency: ms (from health check)
  - Check-in: "Checked in" / "Not checked in"
  - System mode: indicator если system key active
  - Версия Studio

### 1.9 — Sidebar: Agents activity

- [ ] Sidebar content для "Agents" activity:
  - Tree: connections grouped by status (online, offline)
  - Click → set active
  - Right-click context menu: Edit, Remove, Refresh, "Add new"

### 1.10 — Settings: scan range

- [ ] `components/settings/ScanSettings.vue`:
  - Port range start/end (default 8421-8521)
  - Legacy port 5000 toggle (default on)
  - Process scan toggle (default on)
  - Auto-scan on startup toggle (default on)
  - Concurrent scans (default 50)
  - Timeout ms (default 300)

### 1.11 — Tests

- [ ] Unit: ConnectionManager add/remove/health, AgentScanner port scan logic (mock TCP)
- [ ] E2E: scan discovers mock agent, add connection, checkin, switch active, remove

## Acceptance criteria

- [ ] "Scan for agents" находит запущенного агента на 8421 (и 5000 legacy)
- [ ] Process scan находит `Hercules.WebApi.exe` и его порты
- [ ] Add Connection: ввод URL + contribute key → test → save → checkin → агент в списке
- [ ] AgentList: показывает статус (online), check-in, latency
- [ ] Переключение active agent меняет контекст всех views
- [ ] Health polling каждые 30s обновляет статус
- [ ] При закрытии Studio → checkout отправлен (best-effort)
- [ ] При crash Studio → heartbeat не пришёл → агент auto checkout через 60s
- [ ] Вторая Studio пытается подключиться к checked-out агенту → отказ "checked out by {name}"
- [ ] System key: ввод → "system mode" индикатор → system endpoints доступны
- [ ] Force checkout с system key: освобождает агента
- [ ] Settings: scan range настраивается, сохраняется
- [ ] `npm run build` + `biome check` + `vitest run` проходят

## Scope / Likely files

- `electron/main/scanner.ts`, `electron/main/connections.ts`
- `renderer/src/views/AgentList.vue`, `views/DiscoverAgents.vue`
- `renderer/src/components/connections/`, `components/common/SystemKeyDialog.vue`
- `renderer/src/stores/connections.ts`

## Dependencies

- **Backend:** task_096 (port 8421), task_097 (dual API keys), task_098 (CheckIn/CheckOut)
- **Studio:** Stage 0

## Risks / Rollback

- **Port scan false positives:** другой сервис на 8421 → проверка manifest (agentId) отфильтрует
- **Process scan permissions:** может требовать admin для Get-NetTCPConnection → fallback на port-only scan
- **safeStorage на Windows:** Credential Manager может быть заблокирован групповой политикой → fallback на encrypted file

## Links

- Epic: [../README.md](../README.md)
- Stage 0: [stage_00_skeleton.md](stage_00_skeleton.md)
- System Design §7 (Auth): [../SYSTEM-DESIGN.md#7-auth-модель](../SYSTEM-DESIGN.md#7-auth-модель)
- ADR-0004 (Dual API keys): [../adr/0004-dual-api-keys.md](../adr/0004-dual-api-keys.md)
- ADR-0005 (CheckIn/CheckOut): [../adr/0005-checkin-checkout.md](../adr/0005-checkin-checkout.md)