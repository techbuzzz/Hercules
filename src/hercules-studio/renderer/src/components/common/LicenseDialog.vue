<script setup lang="ts">
import { ref } from "vue";
import { useI18n } from "vue-i18n";

const { t } = useI18n();

const emit = defineEmits<{ accepted: [] }>();

const type = ref<"nonprofit" | "commercial">("nonprofit");
const licenseKey = ref("");
const showKeyInput = ref(false);
const declined = ref(false);

async function accept() {
  await window.studioAPI.license.acceptConsent(type.value, type.value === "commercial" ? licenseKey.value : undefined);
  emit("accepted");
}

function showCommercial() {
  type.value = "commercial";
  showKeyInput.value = true;
}
</script>

<template>
  <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/70">
    <div class="w-full max-w-lg rounded-xl border border-app bg-secondary p-6 shadow-2xl">
      <h2 class="mb-3 text-lg font-semibold text-app">{{ t("license.title") }}</h2>

      <p class="mb-4 text-sm text-secondary">{{ t("license.agplText") }}</p>

      <!-- Non-profit checkbox -->
      <label class="mb-3 flex items-start gap-2 text-sm text-app">
        <input
          v-model="type"
          type="radio"
          value="nonprofit"
          class="mt-0.5"
          @change="showKeyInput = false"
        />
        <span>{{ t("license.nonprofit") }}</span>
      </label>

      <!-- Commercial key -->
      <label class="mb-3 flex items-start gap-2 text-sm text-app">
        <input
          v-model="type"
          type="radio"
          value="commercial"
          class="mt-0.5"
          @change="showCommercial"
        />
        <span>{{ t("license.commercialKey") }}</span>
      </label>

      <div v-if="showKeyInput" class="mb-4 pl-6">
        <input
          v-model="licenseKey"
          type="text"
          :placeholder="t('license.enterKey')"
          class="w-full rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
        />
      </div>

      <!-- Decline message -->
      <p v-if="declined" class="mb-3 text-sm text-red-400">{{ t("license.declineMsg") }}</p>

      <!-- Actions -->
      <div class="flex justify-end gap-2">
        <button
          class="rounded-lg px-4 py-2 text-sm text-secondary hover:bg-tertiary"
          @click="declined = true"
        >
          {{ t("license.decline") }}
        </button>
        <button
          class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-500"
          @click="accept"
        >
          {{ t("license.acceptNonprofit") }}
        </button>
      </div>
    </div>
  </div>
</template>