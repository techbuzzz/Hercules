import { defineStore } from "pinia";
import { ref, computed } from "vue";
import { platform, AgentError } from "@renderer/platform";
import type {
  Connection,
  DiscoveredAgent,
  HealthStatus,
  NewConnection,
  ScanProgress,
} from "@renderer/platform/capabilities";
import { HerculesClient } from "../sdk/client";
import { useToastStore } from "./toast";

/** Agent CheckIn TTL is 60s; refresh well inside that window. */
const HEARTBEAT_INTERVAL_MS = 20_000;
/** Never poll health faster than this for one connection. */
const HEALTH_MIN_INTERVAL_MS = 5_000;
const BACKOFF_MIN_MS = 1_000;
const BACKOFF_MAX_MS = 30_000;

export interface BusyAgent {
  connectionId: string;
  /** Whoever currently holds the exclusive contribute slot, per the agent. */
  holder: string | null;
}

export const useConnectionsStore = defineStore("connections", () => {
  const list = ref<Connection[]>([]);
  const activeId = ref<string | null>(null);
  const discovered = ref<DiscoveredAgent[]>([]);
  const scanning = ref(false);
  const scanProgress = ref<ScanProgress | null>(null);
  const loading = ref(false);
  const showAddForm = ref(false);
  /** Values the add-connection dialog should prefill (e.g. from a scan result). */
  const addFormPrefill = ref<{ name: string; baseUrl: string } | null>(null);
  const client = ref<HerculesClient | null>(null);
  const error = ref<string | null>(null);
  const busyAgent = ref<BusyAgent | null>(null);
  /** Set when a session token dies, so the UI can ask for the key again. */
  const sessionExpiredId = ref<string | null>(null);

  const active = computed(() => list.value.find((c) => c.id === activeId.value) ?? null);
  const onlineCount = computed(() => list.value.filter((c) => c.status === "online").length);

  /** Clients built on demand for connections that are not the active one. */
  const extraClients = new Map<string, HerculesClient>();

  /**
   * Returns a client for *any* connection, not just the active one.
   *
   * Stage 7 consensus fans a single prompt out to several agents at once, which the
   * single `client` ref cannot express. Clients are cached per connection so a second
   * call does not reallocate, and the token closure reads the platform map per request
   * so a re-exchange is picked up without rebuilding — the same contract as the
   * active client built in `setActive`.
   *
   * Returns null for an unknown connection or one with no session yet, so callers can
   * report "not connected" instead of firing an unauthenticated request.
   */
  function clientFor(id: string): HerculesClient | null {
    const conn = list.value.find((c) => c.id === id);
    if (!conn) return null;
    if (!platform.session.token(id)) return null;

    const cached = extraClients.get(id);
    if (cached) return cached;

    const built = new HerculesClient(conn.baseUrl, () => platform.session.token(id));
    extraClients.set(id, built);
    return built;
  }

  /** Drops a cached non-active client, e.g. after its session was invalidated. */
  function forgetClient(id: string): void {
    extraClients.delete(id);
  }

  let heartbeatTimer: ReturnType<typeof setInterval> | null = null;
  let monitorTimer: ReturnType<typeof setTimeout> | null = null;
  let unsubscribeProgress: (() => void) | null = null;
  let unsubscribeExpiry: (() => void) | null = null;
  const lastHealthAt = new Map<string, number>();

  async function load(): Promise<void> {
    loading.value = true;
    error.value = null;
    try {
      list.value = await platform.connections.list();
      // Captured in a local before the await below: `setActive` is async, so the list
      // can be replaced by `refresh`/`remove` while it is in flight.
      const first = list.value[0];
      if (first && !activeId.value) {
        await setActive(first.id);
      }
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e);
    } finally {
      loading.value = false;
    }
  }

  async function add(conn: NewConnection): Promise<void> {
    const toast = useToastStore();
    try {
      const created = await platform.connections.add(conn);
      list.value.push(created);
      if (!activeId.value) await setActive(created.id);
      showAddForm.value = false;
      if (created.status === "offline") {
        toast.warn(`Added ${created.name} — agent not reachable yet.`);
      }
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      error.value = msg;
      toast.error(`Failed to add connection: ${msg}`);
      throw e;
    }
  }

  async function remove(id: string): Promise<void> {
    const toast = useToastStore();
    try {
      if (activeId.value === id) {
        stopHeartbeat();
        await checkout();
      }
      await platform.connections.remove(id);
      list.value = list.value.filter((c) => c.id !== id);
      lastHealthAt.delete(id);
      forgetClient(id);

      if (activeId.value === id) {
        activeId.value = null;
        client.value = null;
        const next = list.value[0];
        if (next) await setActive(next.id);
      }
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      error.value = msg;
      toast.error(`Failed to remove: ${msg}`);
      throw e;
    }
  }

  async function setActive(id: string): Promise<void> {
    if (activeId.value === id && client.value) return;
    if (activeId.value) {
      stopHeartbeat();
      await checkout();
    }
    activeId.value = id;

    const conn = list.value.find((c) => c.id === id);
    if (!conn) return;

    // The token lives only in the platform's memory map; this closure reads it
    // per request so a re-exchange is picked up without rebuilding the client.
    client.value = new HerculesClient(conn.baseUrl, () => platform.session.token(id));

    if (platform.session.token(id)) {
      void checkin(conn);
      startHeartbeat();
    }
  }

  async function healthCheck(id: string): Promise<HealthStatus> {
    const now = Date.now();
    const last = lastHealthAt.get(id) ?? 0;
    if (now - last < HEALTH_MIN_INTERVAL_MS) {
      const conn = list.value.find((c) => c.id === id);
      return {
        online: conn?.status === "online",
        agentId: conn?.agentId ?? null,
        displayName: conn?.displayName ?? null,
        latencyMs: null,
        error: null,
      };
    }
    lastHealthAt.set(id, now);

    const status = await platform.connections.healthCheck(id);
    const idx = list.value.findIndex((c) => c.id === id);
    const entry = idx === -1 ? undefined : list.value[idx];
    if (entry) {
      entry.status = status.online ? "online" : "offline";
      entry.lastSeen = new Date().toISOString();
    }
    return status;
  }

  /** Opens the app-level add-connection dialog, optionally prefilled. */
  function openAddForm(init?: { name?: string; baseUrl?: string }): void {
    addFormPrefill.value = init ? { name: init.name ?? "", baseUrl: init.baseUrl ?? "" } : null;
    showAddForm.value = true;
  }

  function closeAddForm(): void {
    showAddForm.value = false;
    addFormPrefill.value = null;
  }

  async function scan(): Promise<void> {
    scanning.value = true;
    error.value = null;
    discovered.value = [];
    try {
      unsubscribeProgress?.();
      unsubscribeProgress = platform.scanner.onProgress((p) => {
        scanProgress.value = p;
      });
      discovered.value = await platform.scanner.discover();
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e);
    } finally {
      unsubscribeProgress?.();
      unsubscribeProgress = null;
      scanning.value = false;
    }
  }

  /** Re-runs the key exchange for a connection whose session token died. */
  async function reauthenticate(id: string, apiKey: string): Promise<void> {
    const conn = list.value.find((c) => c.id === id);
    if (!conn) return;
    try {
      await platform.session.exchange(id, conn.baseUrl, apiKey);
      sessionExpiredId.value = null;
      const idx = list.value.findIndex((c) => c.id === id);
      const entry = idx === -1 ? undefined : list.value[idx];
      if (entry) entry.hasSession = true;
      if (activeId.value === id) {
        startHeartbeat();
        void checkin(conn);
      }
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      useToastStore().error(`Authentication failed: ${msg}`);
      throw e;
    }
  }

  function acknowledgeExpiry(): void {
    sessionExpiredId.value = null;
  }

  function dispose(): void {
    stopHeartbeat();
    stopMonitor();
    unsubscribeProgress?.();
    unsubscribeProgress = null;
    unsubscribeExpiry?.();
    unsubscribeExpiry = null;
  }

  // ---- CheckIn / CheckOut ----

  async function checkin(conn: Connection): Promise<void> {
    const token = platform.session.token(conn.id);
    if (!token) return;
    try {
      const res = await platformFetch(conn, "/api/system/checkin", "POST", token, {
        studioId: platform.app.studioId(),
        studioName: "Hercules Studio",
      });
      if (res?.checkoutRequired || res?.busy) {
        busyAgent.value = {
          connectionId: conn.id,
          holder: typeof res.holder === "string" ? res.holder : null,
        };
      }
      const idx = list.value.findIndex((c) => c.id === conn.id);
      const entry = idx === -1 ? undefined : list.value[idx];
      if (entry) {
        entry.checkedOut = true;
        entry.checkedOutBy = platform.app.studioId();
      }
    } catch (e) {
      if (e instanceof AgentError && (e.status === 409 || e.status === 423)) {
        // Exclusive slot is held elsewhere — a normal condition, not an error.
        busyAgent.value = { connectionId: conn.id, holder: null };
        return;
      }
      // Transient failure: heartbeat will retry.
    }
  }

  async function checkout(): Promise<void> {
    const conn = active.value;
    if (!conn) return;
    const token = platform.session.token(conn.id);
    if (!token) return;
    try {
      await platformFetch(conn, "/api/system/checkout", "POST", token, {
        studioId: platform.app.studioId(),
        studioName: "Hercules Studio",
      });
    } catch {
      // Best-effort: the 60s TTL will release the slot anyway.
    }
    const idx = list.value.findIndex((c) => c.id === conn.id);
    const entry = idx === -1 ? undefined : list.value[idx];
    if (entry) entry.checkedOut = false;
  }

  function startHeartbeat(): void {
    stopHeartbeat();

    const tick = async () => {
      // A backgrounded tab still needs to refresh before the TTL lapses.
      const conn = active.value;
      if (conn) await checkin(conn);
    };
    heartbeatTimer = setInterval(() => void tick(), HEARTBEAT_INTERVAL_MS);

    // Browsers throttle timers in background tabs; re-arm on return.
    document.addEventListener("visibilitychange", onVisibility);
  }

  function stopHeartbeat(): void {
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer);
      heartbeatTimer = null;
    }
    document.removeEventListener("visibilitychange", onVisibility);
  }

  function onVisibility(): void {
    if (document.visibilityState === "visible" && heartbeatTimer) {
      void (async () => {
        const conn = active.value;
        if (conn) await checkin(conn);
      })();
    }
  }

  /**
   * Polls the active agent with exponential backoff. The first probe is fast so
   * a restarted agent is picked up quickly; sustained failure backs off to 30s
   * so an offline agent does not burn requests.
   */
  function startMonitor(): void {
    stopMonitor();
    let delay = BACKOFF_MIN_MS;

    const probe = async (): Promise<void> => {
      const conn = active.value;
      if (!conn || document.visibilityState === "hidden") {
        monitorTimer = setTimeout(() => void probe(), delay);
        return;
      }
      const health = await healthCheck(conn.id);
      delay = health.online ? BACKOFF_MIN_MS : Math.min(delay * 2, BACKOFF_MAX_MS);
      monitorTimer = setTimeout(() => void probe(), delay);
    };

    monitorTimer = setTimeout(() => void probe(), BACKOFF_MIN_MS);
  }

  function stopMonitor(): void {
    if (monitorTimer) {
      clearTimeout(monitorTimer);
      monitorTimer = null;
    }
  }

  /** Wire once at app start so token expiry prompts for a key. */
  function initSessionWatcher(): void {
    unsubscribeExpiry?.();
    unsubscribeExpiry = platform.session.onExpired((connectionId) => {
      sessionExpiredId.value = connectionId;
      const idx = list.value.findIndex((c) => c.id === connectionId);
      const entry = idx === -1 ? undefined : list.value[idx];
      if (entry) entry.hasSession = false;
      useToastStore().warn("Session expired — re-enter the API key to continue.");
    });
  }

  async function platformFetch(
    conn: Connection,
    path: string,
    method: "POST",
    token: string,
    body: unknown,
  ): Promise<Record<string, unknown> | null> {
    const res = await fetch(`${conn.baseUrl}${path}`, {
      method,
      headers: { "Content-Type": "application/json", "X-Session-Token": token },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) throw new AgentError(`HTTP ${res.status}`, res.status);
    if (res.status === 204) return null;
    try {
      return (await res.json()) as Record<string, unknown>;
    } catch {
      return null;
    }
  }

  return {
    list,
    activeId,
    active,
    onlineCount,
    discovered,
    scanning,
    scanProgress,
    loading,
    showAddForm,
    addFormPrefill,
    openAddForm,
    closeAddForm,
    client,
    clientFor,
    forgetClient,
    error,
    busyAgent,
    sessionExpiredId,
    load,
    add,
    remove,
    setActive,
    healthCheck,
    scan,
    reauthenticate,
    acknowledgeExpiry,
    startMonitor,
    dispose,
    initSessionWatcher,
  };
});