import { ref } from "vue";
import { platform } from "../platform";

/**
 * Desktop notifications for long-running operations (Stage 7).
 *
 * Only fires when the page is hidden: if the operator is looking at Studio, a toast is
 * already the right feedback and a system notification would be noise. Asking for
 * permission is also deferred until the first genuinely async completion, so the browser
 * prompt is never spent on someone who never runs anything long.
 */

const hidden = ref(false);

if (typeof document !== "undefined") {
  const sync = () => {
    hidden.value = document.visibilityState === "hidden";
  };
  document.addEventListener("visibilitychange", sync);
  sync();
}

export function useNotifications() {
  const supported = platform.notify.supported();

  async function notify(title: string, body: string): Promise<void> {
    if (!supported) return;

    // Permission is only ever requested while the page is visible — a user who is
    // looking at Studio can answer, and browsers ignore the prompt otherwise. So a
    // hidden page with an undecided permission simply skips this round.
    if (platform.notify.permission() === "default" && !hidden.value) {
      await platform.notify.request();
    }
    if (platform.notify.permission() !== "granted" || !hidden.value) return;

    await platform.notify.show(title, body);
  }

  return { supported, notify, hidden };
}