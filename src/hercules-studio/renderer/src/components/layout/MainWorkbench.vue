<script setup lang="ts">
import { computed, shallowRef, watch, h, defineAsyncComponent, defineComponent } from "vue";
import { useConnectionsStore } from "../../stores/connections";
import { getView } from "../../config/view-registry";
import EmptyState from "../../views/EmptyState.vue";
import { useErrorStore } from "../../stores/error";

const props = defineProps<{ activeView: string }>();

const connections = useConnectionsStore();
const errorStore = useErrorStore();

const currentView = computed(() => {
  if (connections.list.length === 0 && props.activeView === "agents") {
    return "empty";
  }
  return props.activeView;
});

// Loading component
const LoadingView = defineComponent({
  name: "LoadingView",
  render() {
    return h("div", { class: "flex flex-1 items-center justify-center text-secondary" }, [
      h("div", { class: "text-center" }, [
        h("div", {
          class:
            "mx-auto mb-2 h-6 w-6 animate-spin rounded-full border-2 border-zinc-600 border-t-emerald-500",
        }),
        h("p", { class: "text-sm" }, "Loading..."),
      ]),
    ]);
  },
});

// Error component
const ErrorView = defineComponent({
  name: "ErrorView",
  render() {
    return h("div", { class: "flex flex-1 items-center justify-center text-red-400" }, [
      h("div", { class: "text-center" }, [h("p", { class: "text-sm" }, "Failed to load view.")]),
    ]);
  },
});

const lazyComponent = shallowRef<ReturnType<typeof defineAsyncComponent> | null>(null);

watch(
  () => currentView.value,
  (viewId) => {
    if (viewId === "empty" || !viewId) {
      lazyComponent.value = null;
      return;
    }
    const viewDef = getView(viewId);
    if (!viewDef) {
      lazyComponent.value = null;
      return;
    }
    lazyComponent.value = defineAsyncComponent({
      loader: viewDef.component,
      loadingComponent: LoadingView,
      errorComponent: ErrorView,
      timeout: 10000,
      onError: (err: Error) => {
        errorStore.report(`Failed to load view: ${viewId}`, "MainWorkbench", err?.stack);
      },
    });
  },
  { immediate: true },
);
</script>

<template>
  <div class="flex flex-1 flex-col overflow-hidden bg-app">
    <EmptyState v-if="currentView === 'empty'" />
    <component :is="lazyComponent" v-else-if="lazyComponent" :view-id="currentView" />
    <div v-else class="flex flex-1 items-center justify-center text-secondary">
      <p class="text-sm">View &quot;{{ currentView }}&quot; not found.</p>
    </div>
  </div>
</template>