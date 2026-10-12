const fs = require("node:fs");
const path = require("node:path");

// Prepends a status banner to each Stage task file. The checkboxes in these files were
// never ticked during the web-first migration, so opening one implies nothing has been
// built — which is exactly how "243 open items" kept being cited as outstanding work.
const dir = path.join(__dirname, "..", "docs", "EPIC_Hercules_Studio", "tasks");

const STATUS = {
  "stage_00_skeleton.md": "codegen pipeline (openapi-typescript + contract test + generated types) is **done**; the Orval-generated client is not",
  "stage_01_agent_scanner.md": "superseded by A2A discovery (`/agent.manifest.json`)",
  "stage_02_chat_skills.md": "Monaco editor and prompt history with Restore are **done**; the diff view is not",
  "stage_03_skill_push.md": "not built",
  "stage_04_mesh_explorer.md": "canvas, node details, context menu, shared memory and circuit breakers are **done**",
  "stage_05_tools_mcp.md": "tool toggle and MCP list/add/edit/delete are **done**; MCP pre-check before push is not",
  "stage_06_config_restart.md": "raw editor, LLM, roles, mesh inspector, quotas, context, restart and the session-store editor are **done**; Postgres hot-switch is not",
  "stage_07_consensus.md": "parallel fan-out with manual and LLM-judge aggregation is **done**; voting/merge and notifications are not",
  "stage_08_workflow.md": "graph model, validation, editor and in-place update are **done**; the executor (task_105) is not",
  "stage_09_packaging.md": "CI is **done**; desktop packaging is cancelled in favour of the PWA",
};

const MARKER = "<!-- web-first-migration-status -->";

for (const [file, status] of Object.entries(STATUS)) {
  const full = path.join(dir, file);
  let text = fs.readFileSync(full, "utf8");
  if (text.includes(MARKER)) {
    console.log(`${file}: banner already present`);
    continue;
  }

  const banner =
    `${MARKER}\n` +
    `> ⚠️ **These checkboxes are not a progress report.** They were never ticked during the\n` +
    `> web-first migration, so an unticked box does not mean the work is outstanding.\n` +
    `> As of 2026-10-06: ${status}.\n` +
    `> Per-item breakdown: [VERIFICATION.md](../VERIFICATION.md#what-remains).\n\n`;

  // Insert after the H1 title so the document still opens with its name.
  const titleEnd = text.indexOf("\n");
  text = text.slice(0, titleEnd + 1) + "\n" + banner + text.slice(titleEnd + 1);
  fs.writeFileSync(full, text);
  console.log(`${file}: banner added`);
}