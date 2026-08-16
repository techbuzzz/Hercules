import { defineStore } from "pinia";
import { ref, computed } from "vue";

export interface AppError {
  id: string;
  message: string;
  stack?: string;
  context?: string;
  timestamp: string;
}

export const useErrorStore = defineStore("error", () => {
  const errors = ref<AppError[]>([]);
  const hasErrors = computed(() => errors.value.length > 0);
  const lastError = computed(() => errors.value[errors.value.length - 1] ?? null);

  function report(message: string, context?: string, stack?: string): void {
    const id = `err-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    errors.value.push({
      id,
      message,
      context,
      stack,
      timestamp: new Date().toISOString(),
    });
    // Keep last 50 errors
    if (errors.value.length > 50) {
      errors.value = errors.value.slice(-50);
    }
    // Also log to console
    console.error(`[Studio Error] ${context ?? "unknown"}:`, message, stack ?? "");
  }

  function dismiss(id: string): void {
    errors.value = errors.value.filter((e) => e.id !== id);
  }

  function clear(): void {
    errors.value = [];
  }

  // Global error handler
  function installGlobalHandlers(): void {
    window.addEventListener("error", (e) => {
      report(e.message, "window.error", e.error?.stack);
    });
    window.addEventListener("unhandledrejection", (e) => {
      const msg = e.reason instanceof Error ? e.reason.message : String(e.reason);
      const stack = e.reason instanceof Error ? e.reason.stack : undefined;
      report(msg, "unhandledrejection", stack);
    });
  }

  return { errors, hasErrors, lastError, report, dismiss, clear, installGlobalHandlers };
});