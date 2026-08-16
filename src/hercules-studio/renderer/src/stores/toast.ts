import { defineStore } from "pinia";
import { ref } from "vue";

export type ToastType = "info" | "success" | "warning" | "error";

export interface Toast {
  id: string;
  type: ToastType;
  message: string;
  duration: number;
  dismissable: boolean;
}

export const useToastStore = defineStore("toast", () => {
  const toasts = ref<Toast[]>([]);

  function show(message: string, type: ToastType = "info", duration = 4000): void {
    const id = `toast-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    toasts.value.push({ id, type, message, duration, dismissable: true });
    if (duration > 0) {
      setTimeout(() => dismiss(id), duration);
    }
  }

  function info(message: string, duration?: number): void {
    show(message, "info", duration);
  }

  function success(message: string, duration?: number): void {
    show(message, "success", duration);
  }

  function warning(message: string, duration?: number): void {
    show(message, "warning", duration);
  }

  function error(message: string, duration?: number): void {
    show(message, "error", duration ?? 6000);
  }

  function dismiss(id: string): void {
    toasts.value = toasts.value.filter((t) => t.id !== id);
  }

  function clear(): void {
    toasts.value = [];
  }

  return { toasts, show, info, success, warning, error, dismiss, clear };
});