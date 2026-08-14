import { describe, it, expect } from "vitest";
import { IpcChannels } from "@shared/protocol";

describe("IpcChannels", () => {
  it("should have connection channels", () => {
    expect(IpcChannels.CONNECTIONS_LIST).toBe("connections:list");
    expect(IpcChannels.CONNECTIONS_ADD).toBe("connections:add");
    expect(IpcChannels.CONNECTIONS_REMOVE).toBe("connections:remove");
  });

  it("should have scanner channels", () => {
    expect(IpcChannels.SCANNER_SCAN).toBe("scanner:scan");
    expect(IpcChannels.SCANNER_PROGRESS).toBe("scanner:progress");
  });

  it("should have native channels", () => {
    expect(IpcChannels.NATIVE_READ_FILE).toBe("native:readFile");
    expect(IpcChannels.NATIVE_OPEN_EXTERNAL).toBe("native:openExternal");
  });

  it("should have db channels", () => {
    expect(IpcChannels.DB_QUERY).toBe("db:query");
    expect(IpcChannels.DB_EXECUTE).toBe("db:execute");
  });

  it("should have license channels", () => {
    expect(IpcChannels.LICENSE_GET).toBe("license:get");
    expect(IpcChannels.LICENSE_ACCEPT).toBe("license:accept");
  });
});