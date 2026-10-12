import { defineConfig } from "vitest/config";
import vue from "@vitejs/plugin-vue";
import { resolve } from "node:path";

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      "@renderer": resolve(__dirname, "renderer/src"),
    },
  },
  test: {
    environment: "happy-dom",
    include: ["renderer/src/**/*.test.ts", "shared/**/*.test.ts"],
    setupFiles: ["renderer/src/test/setup.ts"],
    restoreMocks: true,
  },
});