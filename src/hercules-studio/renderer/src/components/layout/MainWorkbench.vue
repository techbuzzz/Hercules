<script setup lang="ts">
import { computed } from "vue";
import EmptyState from "../../views/EmptyState.vue";
import AgentList from "../../views/AgentList.vue";
import { useConnectionsStore } from "../../stores/connections";

const props = defineProps<{ activeView: string }>();

const connections = useConnectionsStore();

const currentView = computed(() => {
  if (connections.list.length === 0 && props.activeView === "agents") {
    return "empty";
  }
  return props.activeView;
});
</script>

<template>
  <div class="flex flex-1 flex-col overflow-hidden bg-app">
    <EmptyState v-if="currentView === 'empty'" />
    <AgentList v-else-if="currentView === 'agents'" />
    <div v-else class="flex flex-1 items-center justify-center text-secondary">
      <div class="text-center">
        <p class="text-lg font-medium">{{ currentView }}</p>
        <p class="text-sm">Coming in next stage...</p>
      </div>
    </div>
  </div>
</template>