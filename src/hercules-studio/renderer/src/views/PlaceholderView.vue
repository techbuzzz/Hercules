<script setup lang="ts">
import { useI18n } from "vue-i18n";

defineProps<{ viewId: string }>();

const { t } = useI18n();
</script>

<template>
  <div class="flex flex-1 flex-col items-center justify-center bg-app p-8 text-secondary">
    <div class="max-w-md text-center">
      <h2 class="mb-2 text-xl font-medium text-app">{{ t(`activity.${viewId}`) }}</h2>
      <p class="mb-4 text-sm">This view is coming in a future stage.</p>
      <p class="text-xs opacity-60">View: {{ viewId }}</p>
    </div>
  </div>
</template>