/**
 * Capability resolution.
 *
 * Studio is a browser SPA, so `web` is the only implementation today. This
 * module is the seam: a future desktop adapter would be selected here without
 * touching any view or store.
 *
 * A desktop adapter was considered and intentionally not written — with the
 * Electron main process removed there is no IPC bridge left to wrap, and the
 * audience is browser-only. See unresolved decision #4 in the migration plan.
 * The contract in `capabilities.ts` is the extension point, not a file.
 */

import type { PlatformCapabilities } from "./capabilities";
import { createWebCapabilities } from "./web";

let instance: PlatformCapabilities | null = null;

/** Returns the process-wide capability implementation, creating it on first use. */
export function resolveCapabilities(): PlatformCapabilities {
  if (!instance) {
    instance = createWebCapabilities();
  }
  return instance;
}

/** Test seam: forces re-resolution on next access. */
export function __resetCapabilities(): void {
  instance = null;
}

export const platform: PlatformCapabilities = new Proxy(
  {} as PlatformCapabilities,
  {
    get(_target, prop, receiver) {
      return Reflect.get(resolveCapabilities(), prop, receiver);
    },
  },
);

export type { PlatformCapabilities } from "./capabilities";
export { AgentError, DEFAULT_SETTINGS, DEFAULT_SCAN_SETTINGS } from "./web";