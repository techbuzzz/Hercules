const fs = require("node:fs");
const path = require("node:path");

// The banner added by stamp-stage-status.cjs would contradict the `**Status:**` field
// sitting immediately below it, so both are set from one source of truth.
const dir = path.join(__dirname, "..", "docs", "EPIC_Hercules_Studio", "tasks");

const STATUS = {
  "stage_00_skeleton.md": "partially delivered — codegen done, Orval client not",
  "stage_01_agent_scanner.md": "superseded by A2A discovery (ADR-0009)",
  "stage_02_chat_skills.md": "partially delivered — Monaco + history done, diff view not",
  "stage_03_skill_push.md": "not started",
  "stage_04_mesh_explorer.md": "delivered — canvas, details, context menu, shared memory, circuits",
  "stage_05_tools_mcp.md": "partially delivered — tool + MCP CRUD done, pre-check not",
  "stage_06_config_restart.md": "partially delivered — editors done, Postgres hot-switch not",
  "stage_07_consensus.md": "partially delivered — fan-out + judge done, voting/merge not",
  "stage_08_workflow.md": "partially delivered — authoring done, executor (task_105) not",
  "stage_09_packaging.md": "cancelled (PWA) except CI, which is done",
};

for (const [file, status] of Object.entries(STATUS)) {
  const full = path.join(dir, file);
  let text = fs.readFileSync(full, "utf8");
  const before = text;

  text = text.replace(/^\*\*Status:\*\*.*$/m, `**Status:** ${status}`);

  if (text === before) console.log(`${file}: NO Status line matched`);
  else {
    fs.writeFileSync(full, text);
    console.log(`${file}: ${status}`);
  }
}