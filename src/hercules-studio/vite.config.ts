import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import { resolve } from "node:path";

export default defineConfig({
  root: "renderer",
  resolve: {
    alias: {
      "@renderer": resolve(__dirname, "renderer/src"),
      "@shared": resolve(__dirname, "shared"),
    },
  },
  plugins: [vue()],
  build: {
    outDir: "../dist",
    emptyOutDir: true,
  },
  server: {
    port: 4322,
    strictPort: true,
  },
});