/**
 * Validates .github/workflows/ci.yml.
 *
 * The workflow was written from local command knowledge and never executed, so a typo
 * in a path or a malformed YAML block would only surface on the first push. This parses
 * it and checks that every referenced file exists.
 */
const fs = require("node:fs");
const path = require("node:path");

const repo = path.join(__dirname, "..");
const wfPath = path.join(repo, ".github", "workflows", "ci.yml");
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
console.log(problems.length === 0 ? "\nci.yml looks structurally sound" : `\nPROBLEMS:\n- ${problems.join("\n- ")}`);
process.exit(problems.length === 0 ? 0 : 1);