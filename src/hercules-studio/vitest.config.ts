import { defineConfig } from "vitest/config";
import vue from "@vitejs/plugin-vue";
import { resolve } from "node:path";

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      "@renderer": resolve(__dirname, "renderer/src"),
      "@shared": resolve(__dirname, "shared"),
    },
  },
  test: {
    environment: "happy-dom",
    include: ["renderer/src/**/*.test.ts", "shared/**/*.test.ts"],
  },
});