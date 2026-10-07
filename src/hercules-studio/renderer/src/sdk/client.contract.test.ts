import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

/**
 * Agent ↔ Studio contract guard (Stage 0: API codegen).
 *
 * The agent's OpenAPI document is generated at build time from the same provider
 * that backs the runtime `/openapi/v1.json`, so it cannot drift from what the
 * server actually serves. This test closes the other half of the loop: every
 * endpoint the hand-written `sdk/client.ts` calls must exist in that document.
 *
 * Why this matters concretely: the committed openapi.json was found stale during
 * the web-first migration — `/api/code/run` and `/api/studio/session` were live on
 * the server but absent from the document, because the artifact is opt-in and
 * nobody had regenerated it. Nothing caught that. This does.
 */

// Vitest runs with cwd at the package root (src/hercules-studio). `__dirname`
// is not available under ESM.
const PKG_ROOT = process.cwd();
const OPENAPI = resolve(PKG_ROOT, "../agent/Hercules.WebApi/openapi.json");
const CLIENT = resolve(PKG_ROOT, "renderer/src/sdk/client.ts");

/**
 * Reduces a client call site or an OpenAPI path to a comparable shape:
 * `/api/tools/${name}/enable?x=1` → `/api/tools/{}/enable`.
 *
 * Query strings must go — the client builds some of them from a trailing
 * interpolation (`/api/escalations/pending${q}`). That is only safe to strip when
 * the remainder still ends in a literal character: if stripping would leave a
 * trailing `/`, the interpolation was a path *segment* (`/api/skills/${id}`) and
 * must be preserved as a parameter.
 */
function shape(path: string): string {
  // `split`/`replace` yield `string | undefined` under noUncheckedIndexedAccess even
  // though both always produce a string here.
  const withoutQuery = path.split("?")[0] ?? path;

  const withoutTrailingVar = withoutQuery.replace(/\$\{[^}]+\}$/, "");
  const trailingVarWasQuery =
    withoutTrailingVar !== withoutQuery && !withoutTrailingVar.endsWith("/");

  const base = trailingVarWasQuery ? withoutTrailingVar : withoutQuery;
  return base.replace(/\$\{[^}]+\}/g, "{}").replace(/\{[^}]+\}/g, "{}");
}

describe("sdk/client.ts ↔ agent OpenAPI contract", () => {
  const doc = JSON.parse(readFileSync(OPENAPI, "utf8")) as {
    paths: Record<string, unknown>;
  };
  const documented = new Set(Object.keys(doc.paths).map(shape));

  // Collect every string literal / template literal in the client that looks like
  // an API path, i.e. starts with "/" and is passed to `request(...)`.
  const source = readFileSync(CLIENT, "utf8");
  const called = new Set<string>();
  for (const match of source.matchAll(/[`"](\/api\/[^`"]*)[`"]/g)) {
    const path = match[1];
    if (path) called.add(path);
  }

  it("finds endpoints in the client to check", () => {
    // Guards against the extractor silently matching nothing after a refactor.
    expect(called.size).toBeGreaterThan(15);
  });

  it("the agent document is not stale", () => {
    // If these are missing, openapi.json was not regenerated after the migration.
    expect(documented.has("/api/code/run")).toBe(true);
    expect(documented.has("/api/studio/session")).toBe(true);
  });

  it("every endpoint the client calls is documented by the agent", () => {
    const undocumented = [...called].filter((p) => !documented.has(shape(p)));
    expect(
      undocumented,
      undocumented.length > 0
        ? "Client calls endpoints the agent does not document. Regenerate with:\n" +
            "  dotnet build src/agent/Hercules.WebApi -p:OpenApiGenerateDocumentOnBuild=true"
        : undefined,
    ).toEqual([]);
  });
});