import { ref, computed } from "vue";
import { useConnectionsStore } from "../stores/connections";

export function useActiveAgent() {
  const connections = useConnectionsStore();
  const client = computed(() => connections.client);
  const activeConnection = computed(() => connections.active);

  return {
    client,
    activeConnection,
  };
}

export function useConnection() {
  const connections = useConnectionsStore();

  return {
    list: connections.list,
    active: connections.active,
    activeId: connections.activeId,
    onlineCount: connections.onlineCount,
    client: connections.client,
    setActive: connections.setActive,
    remove: connections.remove,
    healthCheck: connections.healthCheck,
    scan: connections.scan,
    add: connections.add,
  };
}