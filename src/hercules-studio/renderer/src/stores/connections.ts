import { defineStore } from "pinia";
import { ref, computed } from "vue";
import type { Connection, NewConnection, DiscoveredAgent } from "@shared/protocol";
import { HerculesClient } from "../sdk/client";

export const useConnectionsStore = defineStore("connections", () => {
  const list = ref<Connection[]>([]);
  const activeId = ref<string | null>(null);
  const discovered = ref<DiscoveredAgent[]>([]);
  const scanning = ref(false);
  const showAddForm = ref(false);
  const client = ref<HerculesClient | null>(null);

  const active = computed(() => list.value.find((c) => c.id === activeId.value) ?? null);
  const onlineCount = computed(() => list.value.filter((c) => c.status === "online").length);

  async function load() {
    list.value = await window.studioAPI.connections.list();
  }

  async function add(conn: NewConnection) {
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
  }

  async function remove(id: string) {
    await window.studioAPI.connections.remove(id);
    list.value = list.value.filter((c) => c.id !== id);
    if (activeId.value === id) {
      activeId.value = null;
      client.value = null;
      if (list.value.length > 0) {
        await setActive(list.value[0].id);
      }
    }
  }

  async function setActive(id: string) {
    activeId.value = id;
    await window.studioAPI.connections.setActive(id);
    // Build HerculesClient with contribute key from safeStorage
    const conn = list.value.find((c) => c.id === id);
    if (conn) {
      const key = await window.studioAPI.keys.getContributeKey(id);
      if (key) {
        client.value = new HerculesClient(conn.baseUrl, key);
      }
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

  async function scan() {
    scanning.value = true;
    try {
      discovered.value = await window.studioAPI.scanner.scan();
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
    showAddForm,
    onlineCount,
    client,
    load,
    add,
    remove,
    setActive,
    healthCheck,
    scan,
  };
});