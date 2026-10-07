/**
 * Per-stage summary for the Hercules Studio epic task files.
 *
 * WHY THIS EXISTS, AND WHY IT IS NOT JUST A BOX COUNT: an earlier version printed a
 * bare `TOTAL open=345`, which reads exactly like a backlog. It is not one. The
 * checkboxes in these ten files were never ticked during the web-first migration, and
 * every file carries an explicit banner saying so — so the number is an artefact of
 * authoring style, not of progress. That figure already got mis-cited once as
 * outstanding work; this script exists so it cannot be read as a status report again.
 *
 * The authoritative signals are each file's `**Status:**` line and
 * docs/EPIC_Hercules_Studio/VERIFICATION.md. This prints both, alongside the raw box
 * count for reference, and FAILS if any file has lost its status line or its banner.
 */
const fs = require("node:fs");
const path = require("node:path");

const dir = path.join(__dirname, "..", "docs", "EPIC_Hercules_Studio", "tasks");
const files = fs.readdirSync(dir).filter((f) => /^stage_\d+.*\.md$/.test(f)).sort();

const problems = [];
let totalUnchecked = 0;
let totalTicked = 0;

for (const f of files) {
  const text = fs.readFileSync(path.join(dir, f), "utf8");

  const unchecked = (text.match(/^\s*-\s\[ \]/gm) || []).length;
  const ticked = (text.match(/^\s*-\s\[[xX~]\]/gm) || []).length;
  totalUnchecked += unchecked;
  totalTicked += ticked;

  const status = (text.match(/^\*\*Status:\*\*\s*(.+)$/m) || [])[1];
  const hasBanner =
    text.includes("<!-- web-first-migration-status -->") ||
    text.includes("not a progress report");

  console.log(f);
  if (status) {
    console.log(`  status:  ${status.trim()}`);
  } else {
    console.log("  status:  (none)");
    problems.push(`${f}: no **Status:** line — progress is unreadable`);
  }
  if (!hasBanner) {
    console.log("  banner:  MISSING");
    problems.push(`${f}: lost the "checkboxes are not a progress report" banner`);
  }
  console.log(`  boxes:   ${unchecked} unchecked / ${ticked} ticked  (not a progress report)`);
}

console.log("---");
console.log(`stage files: ${files.length}`);
console.log(`boxes: ${totalUnchecked} unchecked / ${totalTicked} ticked  (not a progress report)`);

if (problems.length === 0) {
  console.log("every stage file carries a status line and the progress-report disclaimer");
  console.log("authoritative progress: docs/EPIC_Hercules_Studio/VERIFICATION.md");
} else {
  console.log(`\nPROBLEMS (${problems.length}):`);
  for (const p of problems) console.log(`  ${p}`);
}

process.exit(problems.length === 0 ? 0 : 1);