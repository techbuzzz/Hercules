import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import tailwindcss from "@tailwindcss/vite";
import { resolve } from "node:path";
import { readFileSync } from "node:fs";

const pkg = JSON.parse(readFileSync(new URL("./package.json", import.meta.url), "utf-8")) as {
  version: string;
};

// Dev port deliberately differs from hercules-web (astro.config.mjs) which owns 4322.
// See docs/EPIC_Hercules_Studio/adr/0009-web-first-studio.md
export const DEV_PORT = 4330;

/**
 * The agent hosts this bundle at /ui (ADR-0009), so the base path must match.
 * Without it Vite emits absolute /assets/... URLs that 404 when the bundle is
 * mounted under /ui. The dev server therefore also serves the app at /ui/.
 */
export const BASE_PATH = "/ui/";

export default defineConfig({
  root: "renderer",
  base: BASE_PATH,
  resolve: {
    alias: {
      "@renderer": resolve(__dirname, "renderer/src"),
    },
  },
  plugins: [vue(), tailwindcss()],
  define: {
    __APP_VERSION__: JSON.stringify(pkg.version),
  },
  build: {
    outDir: "../dist",
    emptyOutDir: true,
    sourcemap: true,
  },
  server: {
    port: DEV_PORT,
    // Bind IPv4 explicitly. Left to its own devices Vite binds ::1 only, so a
    // readiness check on http://127.0.0.1:<port>/ui/ never connects.
    host: "127.0.0.1",
  },
  preview: {
    port: DEV_PORT,
    host: "127.0.0.1",
  },
});