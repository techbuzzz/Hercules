<script setup lang="ts">
/**
 * Stage 3.4 — template gallery for creating a new skill.
 *
 * A template is a starting point, not a finished skill: it creates the skill on the active
 * agent and drops the operator into the normal editor, where the diff/history machinery
 * already applies.
 */
import { useI18n } from "vue-i18n";
import { SKILL_TEMPLATES, type SkillTemplate } from "../../skills/templates";

const emit = defineEmits<{ pick: [template: SkillTemplate] }>();

defineProps<{ disabled?: boolean }>();

const { t } = useI18n();
</script>

<template>
  <div class="rounded-xl border border-app bg-secondary p-4">
    <h3 class="mb-1 text-sm font-medium text-app">{{ t("skills.templatesTitle") }}</h3>
    <p class="mb-3 text-xs text-secondary">{{ t("skills.templatesHint") }}</p>

    <div class="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
      <button
        v-for="tpl in SKILL_TEMPLATES"
        :key="tpl.id"
        type="button"
        class="rounded-lg border border-app bg-app px-3 py-2 text-left transition-colors hover:border-emerald-500/50 disabled:opacity-50"
        :disabled="disabled"
        @click="emit('pick', tpl)"
      >
        <div class="text-xs font-medium text-app">{{ t(tpl.nameKey) }}</div>
        <p class="mt-0.5 text-[11px] text-secondary">{{ t(tpl.descriptionKey) }}</p>
        <p class="mt-1 font-mono text-[10px] text-secondary/70">{{ t(tpl.previewKey) }}</p>
      </button>
    </div>
  </div>
</template>