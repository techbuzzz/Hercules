/*
 * Hercules Studio service worker (ADR-0009, Phase 6).
 *
 * Scope note: Studio is served by the agent at /ui, so this worker is
 * registered at /ui/sw.js and only ever sees same-origin GETs under /ui.
 *
 * Strategy — deliberately conservative, because a badly-cached SPA is far worse
 * than an uncached one:
 *   - navigations   : network-first, fall back to the cached shell when offline
 *   - hashed assets : cache-first (their name changes when the content changes)
 *   - everything else (including /api/*): straight to the network
 *
 * It never touches agent API calls: those are same-origin but must not be
 * cached, and a stale /api/config or a replayed session exchange would be
 * actively harmful.
 */

const VERSION = "studio-v1";
const SHELL = "./";
const ASSET_CACHE = `${VERSION}-assets`;

self.addEventListener("install", (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(ASSET_CACHE);
      // Cache the shell so an offline reload still renders the app frame.
      await cache
        .add(new Request(SHELL, { cache: "reload" }))
        .catch(() => {
          /* shell may not be cacheable yet; runtime caching will pick it up */
        });
      await self.skipWaiting();
    })(),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    (async () => {
      const names = await caches.keys();
      await Promise.all(names.filter((n) => !n.startsWith(VERSION)).map((n) => caches.delete(n)));
      await self.clients.claim();
    })(),
  );
});

self.addEventListener("message", (event) => {
  if (event.data === "skip-waiting") void self.skipWaiting();
});

function isApiRequest(url) {
  return url.pathname.startsWith("/api/") || url.pathname.startsWith("/agent");
}

function isHashedAsset(url) {
  // Vite emits /assets/<name>-<hash>.js — the hash is what makes caching safe.
  return url.pathname.includes("/assets/") && /-[A-Za-z0-9_-]{8,}\.[a-z0-9]+$/.test(url.pathname);
}

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  // Only ever serve our own origin; everything else goes straight through.
  if (url.origin !== self.location.origin) return;
  if (isApiRequest(url)) return;

  if (request.mode === "navigate") {
    event.respondWith(
      (async () => {
        try {
          const fresh = await fetch(request);
          const cache = await caches.open(ASSET_CACHE);
          cache.put(SHELL, fresh.clone());
          return fresh;
        } catch {
          const cached = await caches.match(SHELL);
          return cached ?? Response.error();
        }
      })(),
    );
    return;
  }

  if (isHashedAsset(url)) {
    event.respondWith(
      (async () => {
        const cached = await caches.match(request);
        if (cached) return cached;
        const fresh = await fetch(request);
        const cache = await caches.open(ASSET_CACHE);
        cache.put(request, fresh.clone());
        return fresh;
      })(),
    );
  }
});