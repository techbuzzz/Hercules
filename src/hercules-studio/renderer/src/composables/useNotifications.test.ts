import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const show = vi.fn(async () => {});
const request = vi.fn(async () => "granted" as NotificationPermission);
let permission: NotificationPermission = "granted";
let supported = true;

vi.mock("../platform", () => ({
  platform: {
    notify: {
      supported: () => supported,
      permission: () => permission,
      request,
      show,
    },
  },
}));

// The composable snapshots visibility at module load; each test re-imports it with
// `vi.resetModules()` after stubbing the property on the real happy-dom document.
function setVisibility(state: DocumentVisibilityState): void {
  Object.defineProperty(document, "visibilityState", {
    configurable: true,
    get: () => state,
  });
}

async function loadComposable() {
  vi.resetModules();
  return (await import("./useNotifications")).useNotifications();
}

describe("useNotifications", () => {
  beforeEach(() => {
    show.mockClear();
    request.mockClear();
    permission = "granted";
    supported = true;
  });

  afterEach(() => {
    vi.resetModules();
  });

  it("does nothing when notifications are unsupported", async () => {
    setVisibility("hidden");
    supported = false;
    const { notify } = await loadComposable();
    await notify("t", "b");
    expect(show).not.toHaveBeenCalled();
  });

  it("stays silent while the page is visible — a toast is better feedback", async () => {
    setVisibility("visible");
    const { notify } = await loadComposable();
    await notify("t", "b");
    expect(show).not.toHaveBeenCalled();
  });

  it("notifies when the page is hidden and permission is already granted", async () => {
    setVisibility("hidden");
    const { notify } = await loadComposable();
    await notify("title", "body");
    expect(request).not.toHaveBeenCalled();
    expect(show).toHaveBeenCalledWith("title", "body");
  });

  it("does not request permission in the background, where browsers ignore it", async () => {
    setVisibility("hidden");
    permission = "default";
    const { notify } = await loadComposable();
    await notify("t", "b");
    expect(request).not.toHaveBeenCalled();
    expect(show).not.toHaveBeenCalled();
  });

  it("asks for permission while the page is visible, where an answer is possible", async () => {
    setVisibility("visible");
    permission = "default";
    const { notify } = await loadComposable();
    await notify("t", "b");
    expect(request).toHaveBeenCalledTimes(1);
    // Granted in the same round, but nothing is shown while the page is in front.
    expect(show).not.toHaveBeenCalled();
  });

  it("stays silent when permission was refused", async () => {
    setVisibility("hidden");
    permission = "denied";
    const { notify } = await loadComposable();
    await notify("t", "b");
    expect(show).not.toHaveBeenCalled();
  });
});