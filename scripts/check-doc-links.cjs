/**
 * Checks that every relative markdown link in the repo's docs resolves.
 *
 * Found via a stale-architecture sweep: several docs pointed at a frontend that had
 * been deleted, and backlog.md linked nine task files under `tasks/completed/` that
 * actually lived in `tasks/`. Neither was visible without resolving the links.
 */
const fs = require("node:fs");
const path = require("node:path");

const roots = ["docs", "README.md", "README-RU.md", "CONTRIBUTING.md"].filter((p) =>
  fs.existsSync(path.join(__dirname, "..", p)),
);

const files = [];
for (const root of roots) {
  const full = path.join(__dirname, "..", root);
  if (fs.statSync(full).isFile()) files.push(full);
  else {
    const walk = (dir) => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const p = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(p);
        else if (entry.name.endsWith(".md")) files.push(p);
      }
    };
    walk(full);
  }
}

let broken = 0;
let checked = 0;

for (const file of files) {
  const text = fs.readFileSync(file, "utf8");
  const dir = path.dirname(file);
  for (const m of text.matchAll(/\]\(([^)]+)\)/g)) {
    const href = m[1].trim();
    // Skip URLs, mailto, and pure anchors.
    if (/^(https?:|mailto:|#)/i.test(href)) continue;
    // Strip an optional anchor and title.
    const target = href.split("#")[0].split('"')[0].trim();
    if (!target || /^[a-z]+:/i.test(target)) continue;

    checked++;
    if (!fs.existsSync(path.resolve(dir, decodeURIComponent(target)))) {
      broken++;
      const rel = path.relative(path.join(__dirname, ".."), file);
      console.log(`BROKEN ${rel} -> ${href}`);
    }
  }
}

console.log(`\nchecked ${checked} relative markdown links across ${files.length} files`);
console.log(broken === 0 ? "all resolve" : `${broken} broken`);
process.exit(broken === 0 ? 0 : 1);