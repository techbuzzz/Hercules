import { describe, expect, it } from "vitest";
import en from "../i18n/en.json";
import ru from "../i18n/ru.json";
import { viewRegistry } from "../config/view-registry";

/**
 * Every nav entry renders its `labelKey` directly. When a key is missing, vue-i18n
 * falls back to the key string, so the sidebar silently shows `activity.context` to the
 * user instead of "Context" — which shipped once and was only caught by reading a
 * Playwright accessibility snapshot.
 *
 * This test makes that failure loud and permanent.
 */
const get = (bundle: unknown, path: string): unknown =>
  path.split(".").reduce<unknown>((acc, key) => {
    if (acc == null || typeof acc !== "object") return undefined;
    return (acc as Record<string, unknown>)[key];
  }, bundle);

describe("view registry labels", () => {
  const labels = viewRegistry.map((v) => v.labelKey);

  it("declares at least one view", () => {
    expect(labels.length).toBeGreaterThan(0);
  });

  it.each(labels)("%s resolves in en", (key) => {
    const value = get(en, key);
    expect(typeof value, `${key} missing in en.json`).toBe("string");
    expect(value as string).not.toBe("");
  });

  it.each(labels)("%s resolves in ru", (key) => {
    const value = get(ru, key);
    expect(typeof value, `${key} missing in ru.json`).toBe("string");
    expect(value as string).not.toBe("");
  });

  it("uses unique ids", () => {
    const ids = viewRegistry.map((v) => v.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});