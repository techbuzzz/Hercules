<script setup lang="ts">
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import {
  IconUsers,
  IconMessage,
  IconSkills,
  IconTopology,
  IconTools,
  IconSettings,
  IconWorkflow,
  IconMessages,
} from "@tabler/icons-vue";

const { t } = useI18n();

const emit = defineEmits<{ select: [view: string] }>();

const active = ref("agents");

const items = [
  { id: "agents", icon: IconUsers, label: "activity.agents" },
  { id: "chat", icon: IconMessage, label: "activity.chat" },
  { id: "skills", icon: IconSkills, label: "activity.skills" },
  { id: "mesh", icon: IconTopology, label: "activity.mesh" },
  { id: "tools", icon: IconTools, label: "activity.tools" },
  { id: "config", icon: IconSettings, label: "activity.config" },
  { id: "workflow", icon: IconWorkflow, label: "activity.workflow" },
  { id: "consensus", icon: IconMessages, label: "activity.consensus" },
];

function onSelect(id: string) {
  active.value = id;
  emit("select", id);
}
</script>

<template>
  <div class="flex w-12 flex-col items-center gap-1 border-r border-app bg-secondary py-2">
    <button
      v-for="item in items"
      :key="item.id"
      class="group relative flex h-10 w-10 items-center justify-center rounded-lg transition-colors"
      :class="
        active === item.id
          ? 'bg-tertiary text-app'
          : 'text-secondary hover:bg-tertiary hover:text-app'
      "
      :title="t(item.label)"
      @click="onSelect(item.id)"
    >
      <component :is="item.icon" :size="20" :stroke="1.5" />
      <span
        class="absolute left-12 z-50 whitespace-nowrap rounded bg-tertiary px-2 py-1 text-xs text-app opacity-0 transition-opacity group-hover:opacity-100"
      >
        {{ t(item.label) }}
      </span>
    </button>
  </div>
</template>