<script setup lang="ts">
import { useI18n } from "vue-i18n";
import { IconSearch, IconLink, IconBolt, IconUsers, IconTopology, IconWorkflow } from "@tabler/icons-vue";
import { useConnectionsStore } from "../stores/connections";

const { t } = useI18n();
const connections = useConnectionsStore();

const slides = [
  {
    icon: IconBolt,
    title: "Self-improving agent",
    desc: "Hercules agents learn from every interaction, creating and improving skills automatically.",
  },
  {
    icon: IconUsers,
    title: "Multi-agent management",
    desc: "Connect to multiple agents, switch between them, and manage your entire fleet from one IDE.",
  },
  {
    icon: IconTopology,
    title: "Mesh visualization",
    desc: "See your agent mesh topology, health, routing, and shared memory in real-time.",
  },
  {
    icon: IconWorkflow,
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
      <!-- Logo / title -->
      <h1 class="mb-2 text-3xl font-bold text-app">{{ t("empty.welcome") }}</h1>
      <p class="mb-8 text-secondary">{{ t("empty.welcomeDesc") }}</p>

      <!-- Carousel (static, manual nav) -->
      <div class="mb-8 rounded-xl border border-app bg-secondary p-6">
        <div class="flex items-center gap-4">
          <div class="flex h-12 w-12 shrink-0 items-center justify-center rounded-lg bg-emerald-600/10">
            <component :is="slides[0].icon" :size="24" :stroke="1.5" class="text-emerald-400" />
          </div>
          <div class="text-left">
            <h3 class="font-medium text-app">{{ slides[0].title }}</h3>
            <p class="text-sm text-secondary">{{ slides[0].desc }}</p>
          </div>
        </div>
        <!-- Dots -->
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
          <IconSearch :size="18" :stroke="1.5" />
          {{ t("empty.scanAgents") }}
        </button>
        <button
          class="flex items-center gap-2 rounded-lg border border-app px-5 py-2.5 text-sm font-medium text-app hover:bg-tertiary"
          @click="addConnection"
        >
          <IconLink :size="18" :stroke="1.5" />
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