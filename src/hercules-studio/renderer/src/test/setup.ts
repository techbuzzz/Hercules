import { afterEach, beforeEach } from "vitest";
import { __resetSessionRegistry } from "../platform/web";
import { __resetCapabilities } from "../platform";

// happy-dom does not always provide crypto.randomUUID.
if (typeof globalThis.crypto === "undefined") {
  Object.defineProperty(globalThis, "crypto", { value: {}, writable: true });
}
if (typeof globalThis.crypto.randomUUID !== "function") {
  let counter = 0;
  globalThis.crypto.randomUUID = () => {
    counter += 1;
    const rand = Math.random().toString(36).slice(2, 10);
    return `00000000-0000-4000-8000-${String(counter).padStart(4, "0")}${rand}` as `${string}-${string}-${string}-${string}-${string}`;
  };
}

beforeEach(() => {
  localStorage.clear();
  __resetSessionRegistry();
  __resetCapabilities();
});

afterEach(() => {
  __resetSessionRegistry();
});