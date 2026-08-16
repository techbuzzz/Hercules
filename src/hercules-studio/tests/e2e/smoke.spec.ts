import { test, expect, _electron as electron } from "@playwright/test";
import { join } from "node:path";

test("Studio launches and shows license dialog on first run", async () => {
  const electronApp = await electron.launch({
    args: [join(__dirname, "../../out/main/index.js")],
    env: {
      ...process.env,
      // Use temp userData to ensure first-run (no prior consent)
      HERCULES_STUDIO_TEST: "true",
    },
  });

  const window = await electronApp.firstWindow();

  // License dialog should appear on first run
  await expect(window.locator("text=License Agreement")).toBeVisible({ timeout: 15000 });

  // Accept non-profit
  await window.click("text=Accept (Non-profit)");

  // Empty state should appear
  await expect(window.locator("text=Welcome to Hercules Studio")).toBeVisible({ timeout: 10000 });

  // Activity bar should be visible
  await expect(window.locator("text=Agents")).toBeVisible();

  // Status bar should show "No agent connected"
  await expect(window.locator("text=No agent connected")).toBeVisible();

  await electronApp.close();
});

test("Activity bar has all 8 items", async () => {
  const electronApp = await electron.launch({
    args: [join(__dirname, "../../out/main/index.js")],
  });

  const window = await electronApp.firstWindow();

  // Wait for app to load
  await window.waitForTimeout(3000);

  // Check all activity labels appear in tooltips
  const labels = ["Agents", "Chat", "Skills", "Mesh", "Tools", "Config", "Workflow", "Consensus"];
  for (const label of labels) {
    // Activity bar buttons have title attributes
    const btn = window.locator(`[title="${label}"]`);
    await expect(btn).toBeVisible();
  }

  await electronApp.close();
});