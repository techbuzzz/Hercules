import { defineStore } from "pinia";
import { ref, computed } from "vue";
import type { Connection, NewConnection, DiscoveredAgent } from "@shared/protocol";
import { HerculesClient } from "../sdk/client";
import { useToastStore } from "./toast";

export const useConnectionsStore = defineStore("connections", () => {
  const list = ref<Connection[]>([]);
  const activeId = ref<string | null>(null);
  const discovered = ref<DiscoveredAgent[]>([]);
  const scanning = ref(false);
  const loading = ref(false);
  const showAddForm = ref(false);
  const client = ref<HerculesClient | null>(null);
  const error = ref<string | null>(null);

  const active = computed(() => list.value.find((c) => c.id === activeId.value) ?? null);
  const onlineCount = computed(() => list.value.filter((c) => c.status === "online").length);

  async function load(): Promise<void> {
    loading.value = true;
    error.value = null;
    try {
      list.value = await window.studioAPI.connections.list();
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e);
      console.error("[connections.load]", error.value);
    } finally {
      loading.value = false;
    }
  }

  async function add(conn: NewConnection): Promise<void> {
    const toast = useToastStore();
    try {
      const newConn = await window.studioAPI.connections.add(conn);
      list.value.push(newConn);
      // Store contribute key for later use by HerculesClient (in browser mock)
      if (typeof localStorage !== "undefined") {
        localStorage.setItem(`mock-key-${newConn.id}`, conn.apiKey);
      }
      if (!activeId.value) {
        await setActive(newConn.id);
      }
      showAddForm.value = false;
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
      await window.studioAPI.connections.remove(id);
      list.value = list.value.filter((c) => c.id !== id);
      if (activeId.value === id) {
        activeId.value = null;
        client.value = null;
        if (list.value.length > 0) {
          await setActive(list.value[0].id);
        }
      }
      // Clear mock key
      if (typeof localStorage !== "undefined") {
        localStorage.removeItem(`mock-key-${id}`);
      }
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      error.value = msg;
      toast.error(`Failed to remove: ${msg}`);
      throw e;
    }
  }

  async function setActive(id: string): Promise<void> {
    activeId.value = id;
    try {
      await window.studioAPI.connections.setActive(id);
      // Build HerculesClient with contribute key from safeStorage
      const conn = list.value.find((c) => c.id === id);
      if (conn) {
        const key = await window.studioAPI.keys.getContributeKey(id);
        if (key) {
          client.value = new HerculesClient(conn.baseUrl, key);
        }
      }
    } catch (e) {
      console.error("[connections.setActive]", e);
    }
  }

  async function healthCheck(id: string) {
    const status = await window.studioAPI.connections.healthCheck(id);
    const idx = list.value.findIndex((c) => c.id === id);
    if (idx !== -1) {
      list.value[idx].status = status.online ? "online" : "offline";
      list.value[idx].lastSeen = new Date().toISOString();
    }
    return status;
  }

  async function scan(): Promise<void> {
    scanning.value = true;
    error.value = null;
    try {
      discovered.value = await window.studioAPI.scanner.scan();
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e);
      console.error("[connections.scan]", error.value);
    } finally {
      scanning.value = false;
    }
  }

  return {
    list,
    activeId,
    active,
    discovered,
    scanning,
    loading,
    showAddForm,
    onlineCount,
    client,
    error,
    load,
    add,
    remove,
    setActive,
    healthCheck,
    scan,
  };
});