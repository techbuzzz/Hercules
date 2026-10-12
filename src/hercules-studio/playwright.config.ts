import { defineConfig, devices } from "@playwright/test";

const PORT = 4330;
// vite.config.ts sets base "/ui/" because the agent hosts the bundle at /ui
// (ADR-0009), so the app is served under that prefix in dev and preview alike.
const BASE_URL = `http://127.0.0.1:${PORT}/ui/`;

/**
 * Studio is a browser SPA — E2E runs against Chromium against the built bundle,
 * not against Electron. Agent calls are stubbed with page.route() inside specs.
 */
export default defineConfig({
  testDir: "./tests/e2e",
  timeout: 60000,
  expect: {
    timeout: 10000,
  },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"]],
  use: {
    baseURL: BASE_URL,
    actionTimeout: 10000,
    navigationTimeout: 30000,
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: {
    command: `npm run build && npm run preview -- --port ${PORT} --strictPort`,
    url: BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 180000,
  },
});