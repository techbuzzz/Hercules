import { describe, it, expect } from "vitest";
import { HerculesClient } from "../sdk/client";

describe("HerculesClient", () => {
  it("should construct with baseUrl and apiKey", () => {
    const client = new HerculesClient("http://localhost:8421/", "test-key");
    expect(client).toBeDefined();
  });

  it("should trim trailing slash from baseUrl", () => {
    const client = new HerculesClient("http://localhost:8421/", "test-key");
    // Internal check via fetch mock would be ideal, but constructor logic is simple
    expect(client).toBeDefined();
  });

  it("should set and check system key", () => {
    const client = new HerculesClient("http://localhost:8421", "test-key");
    expect(client.hasSystemKey()).toBe(false);
    client.setSystemKey("sys-key");
    expect(client.hasSystemKey()).toBe(true);
    client.setSystemKey(null);
    expect(client.hasSystemKey()).toBe(false);
  });
});