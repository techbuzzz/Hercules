/**
 * Service worker registration (Phase 6).
 *
 * Registered only in production builds. In dev it would cache hashed assets and
 * make every change require a manual cache purge, which is a bad trade for a
 * tool whose whole point is quick iteration.
 *
 * The worker is deliberately conservative — see sw.js for the caching policy.
 */
export function registerServiceWorker(): void {
  if (!import.meta.env.PROD) return;
  if (!("serviceWorker" in navigator)) return;

  // BASE_URL is "/ui/" (see vite.config.ts) so the worker scope matches where
  // the agent actually serves the app.
  const swUrl = `${import.meta.env.BASE_URL}sw.js`;

  window.addEventListener("load", () => {
    navigator.serviceWorker
      .register(swUrl, {
        // The agent serves /ui/sw.js with `Cache-Control: immutable, max-age=1y`
        // (it is treated like any hashed asset). Without this the browser would
        // reuse the cached worker script for a year and users would never get
        // shell updates. `none` forces a revalidation of sw.js itself on every
        // registration check.
        updateViaCache: "none",
      })
      .catch((err) => {
        // A failed registration must never break the app; offline support is a
        // progressive enhancement.
        console.warn("[studio] service worker registration failed", err);
      });
  });
}