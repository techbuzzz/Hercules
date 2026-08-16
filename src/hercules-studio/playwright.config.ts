import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/e2e",
  timeout: 60000,
  expect: {
    timeout: 10000,
  },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: "list",
  use: {
    actionTimeout: 10000,
    navigationTimeout: 30000,
  },
  projects: [
    {
      name: "electron",
      use: {
        // Playwright Electron config is set per-test via _electron.launch
      },
    },
  ],
});