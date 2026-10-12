<script setup lang="ts">
import { useToastStore } from "../../stores/toast";

const toastStore = useToastStore();

function iconPath(type: string): string {
  switch (type) {
    case "success":
      return "M9 12l2 2 4-4";
    case "warning":
      return "M12 9v4M12 17h.01M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z";
    case "error":
      return "M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z";
    default:
      return "M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z";
  }
}

function colorClass(type: string): string {
  switch (type) {
    case "success":
      return "border-emerald-500/50 bg-emerald-500/10 text-emerald-200";
    case "warning":
      return "border-amber-500/50 bg-amber-500/10 text-amber-200";
    case "error":
      return "border-red-500/50 bg-red-500/10 text-red-200";
    default:
      return "border-sky-500/50 bg-sky-500/10 text-sky-200";
  }
}
</script>

<template>
  <div class="pointer-events-none fixed bottom-6 right-6 z-50 flex max-w-sm flex-col gap-2">
    <div
      v-for="toast in toastStore.toasts"
      :key="toast.id"
      class="pointer-events-auto flex items-start gap-3 rounded-xl border px-4 py-3 text-sm shadow-xl fade-in"
      :class="colorClass(toast.type)"
    >
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.5"
        stroke-linecap="round"
        stroke-linejoin="round"
        class="mt-0.5 shrink-0"
      >
        <path :d="iconPath(toast.type)" />
      </svg>
      <span class="flex-1">{{ toast.message }}</span>
      <button
        v-if="toast.dismissable"
        class="shrink-0 text-current opacity-60 hover:opacity-100"
        @click="toastStore.dismiss(toast.id)"
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M18 6L6 18M6 6l12 12" />
        </svg>
      </button>
    </div>
  </div>
</template>