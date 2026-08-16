<script setup lang="ts">
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";

const { t } = useI18n();
const connections = useConnectionsStore();

const slides = [
  {
    title: "Self-improving agent",
    desc: "Hercules agents learn from every interaction, creating and improving skills automatically.",
  },
  {
    title: "Multi-agent management",
    desc: "Connect to multiple agents, switch between them, and manage your entire fleet from one IDE.",
  },
  {
    title: "Mesh visualization",
    desc: "See your agent mesh topology, health, routing, and shared memory in real-time.",
  },
  {
    title: "BPMN workflows",
    desc: "Design multi-agent workflows with a visual BPMN designer and run them across your mesh.",
  },
];

function scan() {
  connections.scan();
}

function addConnection() {
  connections.showAddForm = true;
}
</script>

<template>
  <div class="flex flex-1 items-center justify-center overflow-y-auto bg-app p-8">
    <div class="max-w-2xl text-center">
      <h1 class="mb-2 text-3xl font-bold text-app">{{ t("empty.welcome") }}</h1>
      <p class="mb-8 text-secondary">{{ t("empty.welcomeDesc") }}</p>

      <!-- Carousel (static, manual nav) -->
      <div class="mb-8 rounded-xl border border-app bg-secondary p-6">
        <div class="flex items-center gap-4">
          <div class="flex h-12 w-12 shrink-0 items-center justify-center rounded-lg bg-emerald-600/10">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#10b981" stroke-width="1.5">
              <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z" />
            </svg>
          </div>
          <div class="text-left">
            <h3 class="font-medium text-app">{{ slides[0].title }}</h3>
            <p class="text-sm text-secondary">{{ slides[0].desc }}</p>
          </div>
        </div>
        <div class="mt-4 flex justify-center gap-1.5">
          <span
            v-for="(_, i) in slides"
            :key="i"
            class="h-1.5 w-1.5 rounded-full"
            :class="i === 0 ? 'bg-emerald-500' : 'bg-zinc-600'"
          />
        </div>
      </div>

      <!-- Actions -->
      <div class="mb-8 flex justify-center gap-3">
        <button
          class="flex items-center gap-2 rounded-lg bg-emerald-600 px-5 py-2.5 text-sm font-medium text-white hover:bg-emerald-500"
          @click="scan"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
            <circle cx="11" cy="11" r="8" />
            <path d="M21 21l-4.35-4.35" />
          </svg>
          {{ t("empty.scanAgents") }}
        </button>
        <button
          class="flex items-center gap-2 rounded-lg border border-app px-5 py-2.5 text-sm font-medium text-app hover:bg-tertiary"
          @click="addConnection"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
            <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" />
            <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" />
          </svg>
          {{ t("empty.addConnection") }}
        </button>
      </div>

      <!-- Quickstart -->
      <div class="rounded-xl border border-app bg-secondary p-4 text-left">
        <h3 class="mb-2 text-sm font-medium text-app">{{ t("empty.quickstart") }}</h3>
        <ol class="space-y-1 text-sm text-secondary">
          <li>{{ t("empty.step1") }}</li>
          <li>{{ t("empty.step2") }}</li>
          <li>{{ t("empty.step3") }}</li>
        </ol>
      </div>
    </div>
  </div>
</template>