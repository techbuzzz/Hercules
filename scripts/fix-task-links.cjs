/**
 * Repairs relative markdown links inside docs/roadmap/tasks/completed/.
 *
 * Those files were moved into `completed/` at some point; their relative links were not
 * updated, so `../backlog.md` resolves to `tasks/backlog.md` instead of
 * `roadmap/backlog.md`. ~314 links across ~104 files are broken this way.
 *
 * The repair is conservative by construction: a link is rewritten only when its current
 * target does NOT exist and an alternative does. Nothing is invented, and a link that
 * cannot be resolved by depth is left alone and reported.
 */
const fs = require("node:fs");
const path = require("node:path");

const tasksDir = path.join(__dirname, "..", "docs", "roadmap", "tasks");
if (!fs.existsSync(tasksDir)) {
  console.log("no tasks/ directory");
  process.exit(0);
}

// `completed/` came first (files were moved into it without updating their links), then
// the same class of breakage in `tasks/` proper.
const targetDirs = [path.join(tasksDir, "completed"), tasksDir].filter((d) =>
  fs.existsSync(d),
);

let fixed = 0;
let unfixable = 0;
const report = [];

for (const dir of targetDirs) {
  for (const name of fs.readdirSync(dir).filter((f) => f.endsWith(".md"))) {
    const file = path.join(dir, name);
    const text = fs.readFileSync(file, "utf8");

    let changed = false;
    const out = text.replace(/\]\(([^)]+)\)/g, (whole, href) => {
      if (/^(https?:|mailto:|#)/i.test(href)) return whole;

      const [target, ...rest] = href.split("#");
      const anchor = rest.length ? "#" + rest.join("#") : "";
      if (!target || /^[a-z]+:/i.test(target)) return whole;

      const decoded = decodeURIComponent(target);
      if (fs.existsSync(path.resolve(dir, decoded))) return whole; // already fine

      // Try progressively shallower parent hops.
      let replacement = null;
      for (let depth = 2; depth <= 5; depth++) {
        const candidate = "../".repeat(depth) + decoded.replace(/^(\.\.\/)+/, "");
        if (fs.existsSync(path.resolve(dir, candidate))) {
          replacement = candidate + anchor;
          break;
        }
      }

      if (!replacement) {
        unfixable++;
        report.push(`${path.relative(tasksDir, file)}: ${target}`);
        return whole;
      }

      fixed++;
      changed = true;
      return `](${replacement})`;
    });

    if (changed) fs.writeFileSync(file, out);
  }
}

console.log(`rewritten links: ${fixed}`);
console.log(`still unresolvable: ${unfixable}`);
if (report.length) console.log(report.slice(0, 20).join("\n"));