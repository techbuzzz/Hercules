import { defineStore } from "pinia";
import { ref } from "vue";

/**
 * Shared UI state.
 *
 * The active view used to live as a local ref in App.vue, which meant no view
 * could navigate to another one (e.g. Chat proposing a skill edit). Keeping it
 * in a store makes cross-view navigation a one-liner.
 */
export const useUiStore = defineStore("ui", () => {
  const activeView = ref("agents");
  /** Skill to open when navigating to the skills view. */
  const focusedSkillId = ref<string | null>(null);
  /** Bumped to ask the active view to reload (e.g. after a skill is created). */
  const refreshToken = ref(0);

  function select(viewId: string): void {
    activeView.value = viewId;
  }

  /** Navigate to a skill. Re-selecting the same view still works as a refresh. */
  function openSkill(skillId: string): void {
    focusedSkillId.value = skillId;
    refreshToken.value += 1;
    activeView.value = "skills";
  }

  function requestRefresh(): void {
    refreshToken.value += 1;
  }

  return { activeView, focusedSkillId, refreshToken, select, openSkill, requestRefresh };
});