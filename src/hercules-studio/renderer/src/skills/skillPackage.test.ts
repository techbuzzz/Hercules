import { describe, expect, it } from "vitest";
import { buildSkillPackage, buildZip, crc32, packageFileName } from "./skillPackage";

const dec = new TextDecoder();

/** Reads the central directory and returns entry names — enough to prove the ZIP is well-formed. */
function centralDirectoryNames(zip: Uint8Array): string[] {
  const view = new DataView(zip.buffer, zip.byteOffset, zip.byteLength);
  // Locate the end-of-central-directory record by scanning backwards for its signature.
  let eocd = -1;
  for (let i = zip.length - 22; i >= 0; i--) {
    if (view.getUint32(i, true) === 0x06054b50) {
      eocd = i;
      break;
    }
  }
  expect(eocd).toBeGreaterThanOrEqual(0);

  const count = view.getUint16(eocd + 10, true);
  let offset = view.getUint32(eocd + 16, true);
  const names: string[] = [];

  for (let i = 0; i < count; i++) {
    expect(view.getUint32(offset, true)).toBe(0x02014b50);
    const nameLen = view.getUint16(offset + 28, true);
    const extraLen = view.getUint16(offset + 30, true);
    const commentLen = view.getUint16(offset + 32, true);
    names.push(dec.decode(zip.subarray(offset + 46, offset + 46 + nameLen)));
    offset += 46 + nameLen + extraLen + commentLen;
  }
  return names;
}

describe("crc32", () => {
  it("matches the known IEEE check value", () => {
    // Standard vector for "123456789".
    expect(crc32(new TextEncoder().encode("123456789"))).toBe(0xcbf43926);
  });

  it("is 0 for empty input", () => {
    expect(crc32(new Uint8Array(0))).toBe(0);
  });
});

describe("buildZip", () => {
  it("writes a well-formed archive with one entry per input", () => {
    const zip = buildZip([{ path: "a/skill.meta.json", data: new TextEncoder().encode("{}") }]);
    expect(centralDirectoryNames(zip)).toEqual(["a/skill.meta.json"]);
  });

  it("is deterministic for identical inputs", () => {
    const entry = { path: "x/y.md", data: new TextEncoder().encode("hello") };
    expect(Array.from(buildZip([entry]))).toEqual(Array.from(buildZip([entry])));
  });

  it("preserves entry contents byte for byte", () => {
    const body = "line one\nline two — ünïcode\n";
    const zip = buildZip([{ path: "d/f.md", data: new TextEncoder().encode(body) }]);
    const text = dec.decode(zip);
    expect(text).toContain(body);
  });
});

describe("buildSkillPackage", () => {
  const base = {
    id: "a1b2c3d4",
    name: "answerer",
    description: "answers things",
    phraseReceivers: ["answer", "reply"],
    prompt: "# Answer\n\nBe concise.",
    version: 2,
    createdAt: "2026-01-01",
  };

  it("places every entry under a directory named after the skill id", () => {
    // The spec requires the top-level directory to equal meta.id; an importer rejects it otherwise.
    const names = centralDirectoryNames(buildSkillPackage(base));
    expect(names).toEqual([
      "a1b2c3d4/skill.meta.json",
      "a1b2c3d4/skill.prompt.md",
      "a1b2c3d4/skill.description.md",
    ]);
    for (const name of names) {
      expect(name.split("/")[0]).toBe(base.id);
    }
  });

  it("writes meta in the snake_case shape the deserializer expects", () => {
    const zip = buildSkillPackage(base);
    const text = dec.decode(zip);
    const start = text.indexOf("{");
    const end = text.indexOf("}", start) + 1;
    const meta = JSON.parse(text.slice(start, end));

    expect(meta.id).toBe(base.id);
    expect(meta.phrase_receivers).toEqual(base.phraseReceivers);
    expect(meta.version).toBe(2);
    expect(meta.deprecated_at).toBeNull();
    // camelCase would be silently dropped by the deserializer.
    expect(meta.phraseReceivers).toBeUndefined();
  });

  it("includes code.cs only when the skill has code", () => {
    expect(centralDirectoryNames(buildSkillPackage(base))).not.toContain("a1b2c3d4/code.cs");
    expect(centralDirectoryNames(buildSkillPackage({ ...base, code: "   " }))).not.toContain(
      "a1b2c3d4/code.cs",
    );
    expect(centralDirectoryNames(buildSkillPackage({ ...base, code: "Console.WriteLine(1);" }))).toContain(
      "a1b2c3d4/code.cs",
    );
  });

  it("names the file per the spec", () => {
    expect(packageFileName("a1b2c3d4", 2)).toBe("a1b2c3d4-v2.skillpkg");
  });
});