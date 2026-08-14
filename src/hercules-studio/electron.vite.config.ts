import { resolve } from "node:path";
import { defineConfig, externalizeDepsPlugin } from "electron-vite";
import vue from "@vitejs/plugin-vue";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  main: {
    plugins: [externalizeDepsPlugin()],
    build: {
      rollupOptions: {
        input: {
          index: resolve(__dirname, "electron/main/index.ts"),
        },
      },
    },
    resolve: {
      alias: {
        "@shared": resolve(__dirname, "shared"),
      },
    },
  },
  preload: {
    plugins: [externalizeDepsPlugin()],
    build: {
      rollupOptions: {
        input: {
          index: resolve(__dirname, "electron/preload/index.ts"),
        },
      },
    },
    resolve: {
      alias: {
        "@shared": resolve(__dirname, "shared"),
      },
    },
  },
  renderer: {
    root: "renderer",
    build: {
      rollupOptions: {
        input: {
          index: resolve(__dirname, "renderer/index.html"),
        },
      },
    },
    resolve: {
      alias: {
        "@renderer": resolve(__dirname, "renderer/src"),
        "@shared": resolve(__dirname, "shared"),
      },
    },
    plugins: [vue(), tailwindcss()],
  },
});