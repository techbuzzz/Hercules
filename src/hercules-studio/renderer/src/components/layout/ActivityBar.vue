<script setup lang="ts">
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import { getOrderedViews } from "../../config/view-registry";
import { useConnectionsStore } from "../../stores/connections";

const { t } = useI18n();
const connections = useConnectionsStore();

const emit = defineEmits<{ select: [view: string] }>();

const active = ref("agents");
const views = getOrderedViews();

function onSelect(id: string): void {
  active.value = id;
  emit("select", id);
}

function isDisabled(viewId: string): boolean {
  const view = views.find((v) => v.id === viewId);
  if (view?.requiresConnection && !connections.active) {
    return true;
  }
  return false;
}
</script>

<template>
  <div class="flex w-12 flex-col items-center gap-1 border-r border-app bg-secondary py-2">
    <button
      v-for="view in views"
      :key="view.id"
      class="group relative flex h-10 w-10 items-center justify-center rounded-lg transition-colors disabled:cursor-not-allowed disabled:opacity-30"
      :class="
        active === view.id
          ? 'bg-tertiary text-app'
          : 'text-secondary hover:bg-tertiary hover:text-app'
      "
      :title="t(view.labelKey)"
      :disabled="isDisabled(view.id)"
      @click="onSelect(view.id)"
    >
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width="20"
        height="20"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.5"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <path :d="view.icon" />
      </svg>
      <span
        class="pointer-events-none absolute left-12 z-50 whitespace-nowrap rounded bg-tertiary px-2 py-1 text-xs text-app opacity-0 transition-opacity group-hover:opacity-100"
      >
        {{ t(view.labelKey) }}
      </span>
    </button>
  </div>
</template>