/**
 * Minimal ZIP writer for `.skillpkg` packages (Stage 3.3).
 *
 * Store-only (method 0) on purpose: a skill package is a handful of small text files, so
 * DEFLATE would cost a dependency for no real saving, and "no compression" keeps the
 * format auditable by eye. .NET's `ZipArchive` reads store entries natively, which is what
 * the agent's importer uses.
 *
 * Layout follows `docs/skill-package-spec.md`: entries live under a top-level directory
 * whose name **must** equal `skill.meta.json`'s `id`, and the meta fields are snake_case.
 */

/** CRC-32 (IEEE 802.3), needed for every ZIP entry header. */
const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let i = 0; i < 256; i++) {
    let c = i;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[i] = c >>> 0;
  }
  return table;
})();

export function crc32(data: Uint8Array): number {
  let crc = 0xffffffff;
  for (let i = 0; i < data.length; i++) {
    crc = CRC_TABLE[(crc ^ data[i]) & 0xff] ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

export interface ZipEntry {
  /** Path inside the archive, e.g. `a1b2c3d4/skill.prompt.md`. */
  path: string;
  data: Uint8Array;
}

class ByteWriter {
  private chunks: Uint8Array[] = [];
  length = 0;

  push(bytes: Uint8Array): void {
    this.chunks.push(bytes);
    this.length += bytes.length;
  }

  u16(value: number): void {
    const b = new Uint8Array(2);
    new DataView(b.buffer).setUint16(0, value, true);
    this.push(b);
  }

  u32(value: number): void {
    const b = new Uint8Array(4);
    new DataView(b.buffer).setUint32(0, value >>> 0, true);
    this.push(b);
  }

  concat(): Uint8Array {
    const out = new Uint8Array(this.length);
    let offset = 0;
    for (const chunk of this.chunks) {
      out.set(chunk, offset);
      offset += chunk.length;
    }
    return out;
  }
}

const encoder = new TextEncoder();

/**
 * Builds a store-only ZIP. `date` is pinned so the same inputs always produce the same
 * bytes — a package that changes hash on every build defeats any later verification.
 */
export function buildZip(entries: ZipEntry[], date = new Date("2026-01-01T00:00:00Z")): Uint8Array {
  const out = new ByteWriter();
  const central: ByteWriter[] = [];
  const dosTime =
    ((date.getUTCHours() << 11) | (date.getUTCMinutes() << 5) | (date.getUTCSeconds() >> 1)) & 0xffff;
  const dosDate =
    (((date.getUTCFullYear() - 1980) << 9) | ((date.getUTCMonth() + 1) << 5) | date.getUTCDate()) &
    0xffff;

  for (const entry of entries) {
    const nameBytes = encoder.encode(entry.path);
    const crc = crc32(entry.data);
    const localOffset = out.length;

    // Local file header: sig, version, flags(0), method(0=store), time, date,
    // crc, sizes, name length, extra length(0).
    out.u32(0x04034b50);
    out.u16(20);
    out.u16(0);
    out.u16(0);
    out.u16(dosTime);
    out.u16(dosDate);
    out.u32(crc);
    out.u32(entry.data.length);
    out.u32(entry.data.length);
    out.u16(nameBytes.length);
    out.u16(0);
    out.push(nameBytes);
    out.push(entry.data);

    const c = new ByteWriter();
    // Central directory header: sig, version made by, version needed, flags, method,
    // time, date, crc, sizes, name length, extra/comment/disk attrs, local offset.
    c.u32(0x02014b50);
    c.u16(20);
    c.u16(20);
    c.u16(0);
    c.u16(0);
    c.u16(dosTime);
    c.u16(dosDate);
    c.u32(crc);
    c.u32(entry.data.length);
    c.u32(entry.data.length);
    c.u16(nameBytes.length);
    c.u16(0);
    c.u16(0);
    c.u16(0);
    c.u16(0);
    c.u32(0);
    c.u32(localOffset);
    c.push(nameBytes);
    central.push(c);
  }

  const centralStart = out.length;
  let centralSize = 0;
  for (const c of central) {
    const bytes = c.concat();
    out.push(bytes);
    centralSize += bytes.length;
  }

  // End of central directory.
  out.u32(0x06054b50);
  out.u16(0);
  out.u16(0);
  out.u16(central.length);
  out.u16(central.length);
  out.u32(centralSize);
  out.u32(centralStart);
  out.u16(0);

  return out.concat();
}

export interface SkillPackageInput {
  id: string;
  name: string;
  description: string;
  phraseReceivers: string[];
  prompt: string;
  version: number;
  createdAt?: string;
  /** Optional C# file for a file-based app, stored as `code.cs`. */
  code?: string | null;
}

/**
 * Assembles a `.skillpkg` from the editor content.
 *
 * The top-level directory is the skill id, which the spec requires to equal `meta.id` — a
 * mismatch is what an importer would reject, so both are derived from the same value.
 */
export function buildSkillPackage(input: SkillPackageInput): Uint8Array {
  const dir = input.id;
  const meta = {
    id: input.id,
    name: input.name,
    description: input.description,
    phrase_receivers: input.phraseReceivers,
    created_at: input.createdAt ?? new Date().toISOString().slice(0, 10),
    version: input.version,
    success_rate: 1.0,
    total_uses: 0,
    deprecated_at: null,
    deprecation_reason: null,
    last_evaluation_score: null,
  };

  const entries: ZipEntry[] = [
    { path: `${dir}/skill.meta.json`, data: encoder.encode(JSON.stringify(meta, null, 2)) },
    { path: `${dir}/skill.prompt.md`, data: encoder.encode(input.prompt) },
    { path: `${dir}/skill.description.md`, data: encoder.encode(input.description) },
  ];

  if (input.code?.trim()) {
    entries.push({ path: `${dir}/code.cs`, data: encoder.encode(input.code) });
  }

  return buildZip(entries);
}

/** `{id}-v{version}.skillpkg`, per the spec's naming rule. */
export function packageFileName(id: string, version: number): string {
  return `${id}-v${version}.skillpkg`;
}