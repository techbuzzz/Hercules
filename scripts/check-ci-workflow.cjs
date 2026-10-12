/**
 * Validates .github/workflows/ci.yml, then every OTHER workflow in that directory.
 *
 * The workflow was written from local command knowledge and never executed, so a typo
 * in a path or a malformed YAML block would only surface on the first push. This parses
 * it and checks that every referenced file exists.
 *
 * WHY THE SECOND PASS EXISTS (D1, 2.0.0 release prep): this script originally
 * hardcoded ci.yml and read nothing else. That blind spot is how `studio-ci.yml` sat
 * in the repo invoking `npm run package:win` and `npm run lint:check` — neither script
 * has existed since ADR-0009 removed the Electron shell — while this guard reported
 * green. A guard that only inspects the file someone remembers is not a guard. So the
 * second pass now sweeps EVERY *.yml here and fails on any `npm run <script>` that
 * package.json does not define.
 */
const fs = require("node:fs");
const path = require("node:path");

const repo = path.join(__dirname, "..");
const wfDir = path.join(repo, ".github", "workflows");
const wfPath = path.join(wfDir, "ci.yml");
const text = fs.readFileSync(wfPath, "utf8");

const problems = [];

// --- Structural checks without a YAML dependency ---
// Only keys under `jobs:`; `on:` has the same 2-space indentation and would be counted.
const jobsSection = text.split(/^jobs:\s*$/m)[1] ?? "";
const jobs = [...jobsSection.matchAll(/^  ([a-z][a-z0-9_-]*):$/gm)].map((m) => m[1]);
if (jobs.length < 3) problems.push(`expected >=3 jobs under jobs:, found ${jobs.length}`);

for (const key of ["name:", "on:", "jobs:"]) {
  if (!new RegExp(`^${key.replace(":", "\\:")}`, "m").test(text)) problems.push(`missing top-level "${key}"`);
}

// Tabs break YAML indentation outright.
if (/\t/.test(text)) problems.push("file contains a tab character");

// --- Every path a `run:` step mentions must exist ---
const referenced = new Set();
for (const m of text.matchAll(/\b((?:[\w.-]+\/)+[\w.-]+|[\w-]+\.(?:json|slnx|yml|ts|ps1))\b/g)) {
  const p = m[1];
  // Skip things that are not repo paths: URL fragments, npm script targets, globs.
  if (p.startsWith("http") || p.includes("*") || p.endsWith(".d.ts")) continue;
  referenced.add(p);
}

const checkable = [...referenced].filter(
  (p) => !["package.json", "package-lock.json", "global.json"].includes(p),
);

// The critical paths CI would fail on: solution, SDK pin and Studio lockfile.
for (const required of ["global.json", "src/agent/Hercules.slnx", "src/hercules-studio/package-lock.json"]) {
  if (!fs.existsSync(path.join(repo, required))) problems.push(`ci.yml references missing file: ${required}`);
}

// The npm scripts CI invokes must exist in package.json.
const pkg = JSON.parse(fs.readFileSync(path.join(repo, "src/hercules-studio/package.json"), "utf8"));
for (const script of ["typecheck", "lint", "test", "build", "test:e2e"]) {
  if (!pkg.scripts[script]) problems.push(`ci.yml runs "npm run ${script}" but package.json has no such script`);
}

console.log(`jobs found: ${jobs.join(", ")}`);
console.log(`npm scripts verified: typecheck, lint, test, build, test:e2e`);

// --- Every other workflow must only invoke npm scripts that exist ---
// Without this, a workflow can rot in silence: studio-ci.yml survived multiple
// web-first migrations calling scripts deleted alongside the Electron shell.
const allWorkflows = fs
  .readdirSync(wfDir)
  .filter((f) => /\.ya?ml$/.test(f))
  .sort();

const scriptProblems = [];
let checkedScripts = 0;

for (const file of allWorkflows) {
  const body = fs.readFileSync(path.join(wfDir, file), "utf8");
  // The pattern requires an explicit `run`, so bare `npm ci` / `npm install` — which
  // package.json does not need to declare — are not matched and need no exemption.
  for (const m of body.matchAll(/\bnpm\s+(?:run|run-script)\s+([A-Za-z0-9:_-]+)/g)) {
    const script = m[1];
    if (!pkg.scripts[script]) {
      scriptProblems.push(
        `${file} runs "npm run ${script}" but src/hercules-studio/package.json has no such script`,
      );
    }
    checkedScripts++;
  }

  // Packaging a desktop installer is only meaningful with an Electron shell, which
  // ADR-0009 removed. If a workflow comes back, make it a deliberate edit, not a copy.
  if (/electron-builder|electron-vite|package:win/i.test(body)) {
    scriptProblems.push(
      `${file} still references the Electron packaging pipeline removed by ADR-0009`,
    );
  }
}

console.log(`workflows scanned: ${allWorkflows.length} (${allWorkflows.join(", ")})`);
console.log(`npm script references verified: ${checkedScripts}`);
problems.push(...scriptProblems);

console.log(problems.length === 0 ? "\nci.yml looks structurally sound" : `\nPROBLEMS:\n- ${problems.join("\n- ")}`);
process.exit(problems.length === 0 ? 0 : 1);
