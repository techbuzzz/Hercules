const fs = require("node:fs");
const path = require("node:path");

const dir = path.join(__dirname, "..", "docs", "EPIC_Hercules_Studio", "tasks");
const files = fs.readdirSync(dir).filter((f) => f.endsWith(".md")).sort();

let totalOpen = 0;
let totalDone = 0;
for (const f of files) {
  const text = fs.readFileSync(path.join(dir, f), "utf8");
  const open = (text.match(/^\s*-\s\[ \]/gm) || []).length;
  const done = (text.match(/^\s*-\s\[[xX~]\]/gm) || []).length;
  totalOpen += open;
  totalDone += done;
  console.log(`${f.padEnd(28)} open=${String(open).padStart(3)}  done/partial=${String(done).padStart(3)}`);
}
console.log("---");
console.log(`TOTAL open=${totalOpen}  done/partial=${totalDone}`);