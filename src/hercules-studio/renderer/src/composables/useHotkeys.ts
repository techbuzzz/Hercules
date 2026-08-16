import { ref, onMounted, onUnmounted } from "vue";

export interface HotkeyHandler {
  (e: KeyboardEvent): void;
}

export interface HotkeyDef {
  ctrl?: boolean;
  shift?: boolean;
  alt?: boolean;
  key: string;
  handler: HotkeyHandler;
  preventDefault?: boolean;
}

export function useHotkeys(hotkeys: HotkeyDef[]) {
  function onKeyDown(e: KeyboardEvent) {
    for (const hk of hotkeys) {
      const ctrlMatch = hk.ctrl ? (e.ctrlKey || e.metaKey) : true;
      const shiftMatch = hk.shift ? e.shiftKey : true;
      const altMatch = hk.alt ? e.altKey : true;
      const keyMatch = e.key.toLowerCase() === hk.key.toLowerCase();

      if (ctrlMatch && shiftMatch && altMatch && keyMatch) {
        if (hk.preventDefault !== false) {
          e.preventDefault();
        }
        hk.handler(e);
        return;
      }
    }
  }

  onMounted(() => {
    window.addEventListener("keydown", onKeyDown);
  });

  onUnmounted(() => {
    window.removeEventListener("keydown", onKeyDown);
  });
}

// Common hotkey presets
export function useGlobalHotkeys(callbacks: {
  onSave?: () => void;
  onSend?: () => void;
  onPalette?: () => void;
  onQuickCommand?: () => void;
  onNew?: () => void;
  onOpen?: () => void;
  onCloseTab?: () => void;
  onNextTab?: () => void;
  onPrevTab?: () => void;
  onSettings?: () => void;
}) {
  useHotkeys([
    { ctrl: true, key: "s", handler: () => callbacks.onSave?.() },
    { ctrl: true, key: "enter", handler: () => callbacks.onSend?.() },
    { ctrl: true, shift: true, key: "p", handler: () => callbacks.onPalette?.() },
    { ctrl: true, key: "k", handler: () => callbacks.onQuickCommand?.() },
    { ctrl: true, key: "n", handler: () => callbacks.onNew?.() },
    { ctrl: true, key: "o", handler: () => callbacks.onOpen?.() },
    { ctrl: true, key: "w", handler: () => callbacks.onCloseTab?.() },
    { ctrl: true, key: "tab", handler: () => callbacks.onNextTab?.() },
    { ctrl: true, shift: true, key: "tab", handler: () => callbacks.onPrevTab?.() },
    { ctrl: true, key: ",", handler: () => callbacks.onSettings?.() },
  ]);
}