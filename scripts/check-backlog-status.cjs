/**
 * Cross-checks docs/roadmap/backlog.md against the task files it links.
 *
 * Found one instance where the backlog marked a task `done` while its file still had 9
 * unticked boxes. This checks every row, because a "done" claim that contradicts the
 * task file is exactly the kind of stale state that keeps being cited as work
 * outstanding (or, worse, hides work that is not done).
 */
const fs = require("node:fs");
const path = require("node:path");

const repoRoot = path.join(__dirname, "..");
const backlogFile = path.join(repoRoot, "docs", "roadmap", "backlog.md");
const backlogDir = path.dirname(backlogFile);

const rows = [];
for (const line of fs.readFileSync(backlogFile, "utf8").split(/\r?\n/)) {
  // | 92 | 48 | [Title](tasks/completed/task_092.md) | `slug` | done |
  const m = line.match(
    /^\|\s*(\d+)\s*\|[^|]*\|\s*\[[^\]]*\]\(([^)]+task_\d+\.md)\)\s*\|\s*`[^`]*`\s*\|\s*([^|]*)\|/,
  );
  if (m) rows.push({ id: m[1], link: m[2], status: m[3].trim() });
}

const mismatches = [];
let checked = 0;

for (const row of rows) {
  const full = path.resolve(backlogDir, row.link);
  if (!fs.existsSync(full)) {
    mismatches.push({ ...row, problem: "linked file does not exist" });
    continue;
  }
  const text = fs.readFileSync(full, "utf8");
  const open = (text.match(/^\s*-\s\[ \]/gm) || []).length;
  const done = (text.match(/^\s*-\s\[[xX]\]/gm) || []).length;

  checked++;
  // A row may legitimately say "done (deferrals tracked elsewhere)" — that is honest
  // only if the qualifier is present. A bare "done" with open boxes is the defect.
  const claimsDone = /^done\b/i.test(row.status);
  const qualified = /\(|\s—\s/.test(row.status);
  if (claimsDone && open > 0 && !qualified) {
    mismatches.push({
      ...row,
      problem: `claims a bare "done" but the task file has ${open} unticked / ${done} ticked`,
    });
  }
}

console.log(`backlog rows checked: ${rows.length}, task files read: ${checked}`);
if (mismatches.length === 0) {
  console.log("every 'done' row is backed by a task file with no open boxes");
} else {
  console.log(`\nMISMATCHES (${mismatches.length}):`);
  for (const m of mismatches) {
    console.log(`  task_${m.id}: ${m.status} — ${m.problem}`);
    console.log(`      ${m.link}`);
  }
}
process.exit(mismatches.length === 0 ? 0 : 1);