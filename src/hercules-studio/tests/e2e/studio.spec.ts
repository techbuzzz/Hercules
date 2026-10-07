import { expect, test, type Page } from "@playwright/test";

/**
 * Studio is a browser SPA (ADR-0009). These specs run against the built bundle
 * in Chromium — never against Electron, which no longer exists.
 *
 * Agent calls are stubbed with page.route() so the suite is hermetic and does
 * not need a running agent. The one thing we deliberately do NOT stub is the
 * credential-storage assertion, because that is the invariant worth defending:
 * no API key may ever reach localStorage.
 */

const AGENT_URL = "http://localhost:8421";

async function stubAgent(page: Page, opts: { sessionStatus?: number } = {}): Promise<void> {
  // Well-known discovery endpoint. 200 => healthy agent.
  await page.route("**/agent.manifest.json", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ agentId: "hercules-main", displayName: "Hercules", version: "1.0.0" }),
    });
  });

  await page.route("**/api/health", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "alive" }) }),
  );

  // Endpoints that views call on mount, so every test stays hermetic without having to
  // know they exist. ConfigView loads keys for any system session; ConsensusView records
  // a finished round; Mesh/Tools/Skills each fetch on mount. Without these a test that
  // merely *visits* a view makes a real network call — which fails silently here, but on
  // a machine with an agent on 8421 would really persist a record.
  // Tests that care about one of these register their own stub later, which wins.
  const emptyList = (extra: Record<string, unknown> = {}) =>
    JSON.stringify({ count: 0, items: [], servers: [], agents: [], keys: [], ...extra });

  await page.route("**/api/auth/keys", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: emptyList({ contributeCount: 0, systemCount: 0, currentFingerprint: null }),
    }),
  );
  await page.route("**/api/consensus/sessions**", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: emptyList() }),
  );
  await page.route("**/api/code/scan", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ allowed: true, reasons: [], lineNumbers: [] }),
    }),
  );
  await page.route("**/api/config", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ config: {}, source: "file" }),
    }),
  );
  await page.route("**/api/skills", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
  );
  await page.route("**/api/tools", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ count: 0, allowedCount: 0, tools: [] }),
    }),
  );
  await page.route("**/api/mcp/servers", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: emptyList() }),
  );
  await page.route("**/api/mesh/shared-memory", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
  );
  await page.route("**/api/mesh/circuits", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
  );
  await page.route("**/api/mesh/agents", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: emptyList() }),
  );
  await page.route("**/api/mesh/denials**", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
  );
  await page.route("**/api/mesh/health", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ agents: [], healthyCount: 0, degradedCount: 0, unhealthyCount: 0 }),
    }),
  );
  await page.route("**/api/mesh/dashboard", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        topology: { agentCount: 0, agents: [], generatedAt: "2026-01-01T00:00:00Z" },
        health: { agents: [], healthyCount: 0, degradedCount: 0, unhealthyCount: 0 },
      }),
    }),
  );

  // Session exchange — the browser exchanges its key for a short-lived token.
  await page.route("**/api/studio/session", (route) =>
    route.fulfill({
      status: opts.sessionStatus ?? 200,
      contentType: "application/json",
      body: JSON.stringify({
        token: "test-session-token",
        role: "contribute",
        agentId: "hercules-main",
        displayName: "Hercules",
        expiresAt: new Date(Date.now() + 1_800_000).toISOString(),
        capabilities: ["chat", "skills:read"],
        ttlSeconds: 1800,
      }),
    }),
  );

  // CheckIn must not throw during the test.
  await page.route("**/api/system/checkin", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) }),
  );
  await page.route("**/api/system/checkout", (route) => route.fulfill({ status: 204, body: "" }));
}

test.beforeEach(async ({ page }) => {
  await stubAgent(page);
});

/** Accepts the license dialog and connects to the stubbed agent. */
async function connect(page: Page): Promise<void> {
  await page.goto("./");
  await page.getByRole("button", { name: /accept/i }).click();
  await page.getByRole("button", { name: /add connection manually/i }).click();
  await page.getByLabel(/^name/i).fill("local-agent");
  await page.getByLabel(/base url/i).fill(AGENT_URL);
  await page.getByLabel(/api key/i).fill("hc_contrib_TEST");
  await page.getByRole("button", { name: /^save$/i }).click();
  await expect(page.getByText(/connected: local-agent/i)).toBeVisible();
}

test.afterEach(async ({ page }) => {
  // Leave no test connection behind for the next spec.
  await page.evaluate(() => localStorage.clear());
});

test("renders the IDE shell and first-run license consent", async ({ page }) => {
  await page.goto("./");

  await expect(page.getByRole("heading", { name: /license agreement/i })).toBeVisible();
  // Activity bar and status bar are part of the shell and must render, i.e.
  // every t() call resolved rather than silently rendering an empty component.
  // `exact` matters: "Agents" otherwise also substring-matches "Scan for agents".
  await expect(page.getByRole("button", { name: "Agents", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Skills", exact: true })).toBeVisible();
  await expect(page.getByText(/v\d+\.\d+\.\d+/)).toBeVisible();
});

test("translations resolve — no component renders empty", async ({ page }) => {
  const errors: string[] = [];
  page.on("console", (m) => {
    if (m.type() === "error") errors.push(m.text());
  });

  await page.goto("./");
  await page.getByRole("button", { name: /accept/i }).click();

  await expect(page.getByRole("heading", { name: /welcome to hercules studio/i })).toBeVisible();
  // A missing translation or a CSP eval failure shows up as a console error.
  expect(errors.filter((e) => /EvalError|unsafe-eval/.test(e))).toHaveLength(0);
});

test("connect flow reaches the agent and stores no credential", async ({ page }) => {
  await page.goto("./");
  await page.getByRole("button", { name: /accept/i }).click();

  // The dialog must be reachable from the empty state, not only from the
  // agents view (which is not mounted while there are zero connections).
  await page.getByRole("button", { name: /add connection manually/i }).click();
  await expect(page.getByRole("heading", { name: /add connection/i })).toBeVisible();

  await page.getByLabel(/^name/i).fill("local-agent");
  await page.getByLabel(/base url/i).fill(AGENT_URL);
  await page.getByLabel(/api key/i).fill("hc_contrib_SUPERSECRET");

  await page.getByRole("button", { name: /^save$/i }).click();

  // Connected: the toast, the online connection row and an unlocked sidebar.
  await expect(page.getByText(/connected: local-agent/i)).toBeVisible();
  await expect(page.getByText("Hercules").first()).toBeVisible();

  const stored = await page.evaluate(() => {
    const out: Record<string, string> = {};
    for (let i = 0; i < localStorage.length; i += 1) {
      const k = localStorage.key(i);
      if (k) out[k] = localStorage.getItem(k) ?? "";
    }
    return JSON.stringify(out);
  });

  // The central security invariant of ADR-0009.
  expect(stored).not.toContain("hc_contrib_SUPERSECRET");
  expect(stored).not.toContain("test-session-token");

  const session = await page.evaluate(() => JSON.stringify(Object.keys(sessionStorage)));
  expect(session).not.toContain("test-session-token");
});

test("rejects an invalid API key without persisting it", async ({ page }) => {
  await stubAgent(page, { sessionStatus: 401 });

  await page.goto("./");
  await page.getByRole("button", { name: /accept/i }).click();
  await page.getByRole("button", { name: /add connection manually/i }).click();

  await page.getByLabel(/^name/i).fill("bad");
  await page.getByLabel(/base url/i).fill(AGENT_URL);
  await page.getByLabel(/api key/i).fill("hc_contrib_WRONG");

  await page.getByRole("button", { name: /^save$/i }).click();

  await expect(page.getByText(/invalid api key/i).first()).toBeVisible();

  const stored = await page.evaluate(() => localStorage.getItem("hercules-studio.connections.v1"));
  expect(stored ?? "").not.toContain("hc_contrib_WRONG");
});

test("skill prompt history is listed and can be restored", async ({ page }) => {
  await stubAgent(page);

  const skill = {
    id: "s1",
    name: "answerer",
    description: "answers things",
    phraseReceivers: ["answer"],
    version: 2,
    successRate: 0.9,
    totalUses: 12,
    createdAt: "2026-01-01T00:00:00Z",
  };
  await page.route("**/api/skills", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([skill]) }),
  );

  let prompt = "Answer concisely.";
  await page.route("**/api/skills/s1", async (route) => {
    if (route.request().method() === "PUT") {
      const body = route.request().postDataJSON() as { prompt?: string };
      if (body?.prompt) prompt = body.prompt;
      return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(skill) });
    }
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        meta: skill,
        descriptionMarkdown: "answers things",
        prompt,
      }),
    });
  });

  const revisions = [
    {
      version: 1,
      prompt: "Be verbose.",
      changedAt: "2026-10-01T10:00:00Z",
      source: "manual",
      author: null,
    },
    {
      version: 2,
      prompt: "Answer concisely.",
      changedAt: "2026-10-02T10:00:00Z",
      source: "llm-improve",
      author: null,
    },
  ];
  let historyCalls = 0;
  await page.route("**/api/skills/s1/prompt-history", (route) => {
    historyCalls += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ skillId: "s1", count: revisions.length, revisions }),
    });
  });

  await connect(page);
  await page.getByRole("button", { name: "Skills", exact: true }).click();

  await page.getByRole("button", { name: /prompt history/i }).click();
  await expect(page.getByText("llm-improve")).toBeVisible();
  await expect(page.getByText("manual")).toBeVisible();

  // Restoring writes the old prompt back through the normal save path.
  // The button is labelled per revision so it is unambiguous in a list and to a screen
  // reader, rather than several identical "Restore" buttons.
  await page.getByRole("button", { name: /restore version 1/i }).click();
  await expect
    .poll(() => historyCalls, { timeout: 10_000 })
    .toBeGreaterThan(0);
});

test("skill prompt revisions diff against the editor draft", async ({ page }) => {
  await stubAgent(page);

  const skill = {
    id: "s1",
    name: "answerer",
    description: "answers things",
    phraseReceivers: ["answer"],
    version: 2,
    successRate: 0.9,
    totalUses: 12,
    createdAt: "2026-01-01T00:00:00Z",
  };
  await page.route("**/api/skills", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([skill]) }),
  );
  await page.route("**/api/skills/s1", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        meta: skill,
        descriptionMarkdown: "answers things",
        prompt: "Answer concisely.\nAlways cite sources.",
      }),
    }),
  );

  await page.route("**/api/skills/s1/prompt-history", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        skillId: "s1",
        count: 2,
        revisions: [
          {
            version: 1,
            prompt: "Be verbose.\nAlways cite sources.",
            changedAt: "2026-10-01T10:00:00Z",
            source: "manual",
            author: null,
          },
          {
            version: 2,
            prompt: "Answer concisely.\nAlways cite sources.",
            changedAt: "2026-10-02T10:00:00Z",
            source: "llm-improve",
            author: null,
          },
        ],
      }),
    }),
  );

  await connect(page);
  await page.getByRole("button", { name: "Skills", exact: true }).click();
  await page.getByRole("button", { name: /prompt history/i }).click();

  // The list renders newest-first, so v2 is identical to the draft — the diff says so
  // instead of rendering a full-file replacement, which is what a naive comparison does.
  await page.getByRole("button", { name: /diff version 2/i }).click();
  const panel = page.getByTestId("prompt-diff");
  await expect(panel).toBeVisible();
  await expect(panel.getByText(/identical to the current prompt/i)).toBeVisible();

  // v1 differs on exactly one line, so the counts are 1 added / 1 removed.
  await page.getByRole("button", { name: /diff version 1/i }).click();
  await expect(panel.getByText("+1")).toBeVisible();
  await expect(panel.getByText("−1")).toBeVisible();
  await expect(panel.getByText("Be verbose.")).toBeVisible();
  // The unchanged line is context, not a delete+insert pair.
  await expect(panel.getByText("Always cite sources.")).toBeVisible();
});

test("skill push fans out to several agents and survives one failure", async ({ page }) => {
  await stubAgent(page);

  const skill = {
    id: "s1",
    name: "answerer",
    description: "answers things",
    phraseReceivers: ["answer"],
    version: 2,
    successRate: 0.9,
    totalUses: 12,
    createdAt: "2026-01-01T00:00:00Z",
  };
  await page.route("**/api/skills", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([skill]) }),
  );
  await page.route("**/api/skills/s1", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        meta: skill,
        descriptionMarkdown: "answers things",
        prompt: "Answer concisely.",
      }),
    }),
  );
  await page.route("**/api/mcp/servers", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        count: 2,
        servers: [
          { name: "filesystem", status: "Healthy", toolCount: 3, config: { enabled: true } },
          { name: "search", status: "Unhealthy", toolCount: 0, config: { enabled: true } },
        ],
      }),
    }),
  );

  // 8422 refuses the update, 8421 accepts it — the push must survive the failure.
  let puts = 0;
  let refused = 0;
  await page.route("**/api/skills/s1", async (route) => {
    if (route.request().method() !== "PUT") return route.fallback();
    const url = route.request().url();
    if (url.includes("8422")) {
      refused += 1;
      return route.fulfill({
        status: 500,
        contentType: "application/json",
        body: JSON.stringify({ error: "read-only agent" }),
      });
    }
    puts += 1;
    return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(skill) });
  });

  await connect(page);

  // A second agent, so cross-agent push has somewhere to go.
  await page.getByRole("button", { name: "Agents", exact: true }).click();
  await page.getByRole("button", { name: /add connection/i }).click();
  await page.getByLabel(/^name/i).fill("beta-agent");
  await page.getByLabel(/base url/i).fill("http://localhost:8422");
  await page.getByLabel(/api key/i).fill("hc_contrib_TEST");
  await page.getByRole("button", { name: /^save$/i }).click();
  await expect(page.getByText(/connected: beta-agent/i)).toBeVisible();

  await page.getByRole("button", { name: "Skills", exact: true }).click();
  await page.getByRole("button", { name: /push to agents/i }).click();

  await page.getByRole("button", { name: /^local-agent$/ }).click();
  await page.getByRole("button", { name: /^beta-agent$/ }).click();

  // MCP pre-check reports per-agent status, including the unhealthy server.
  await page.getByRole("button", { name: /^check$/i }).click();
  await expect(page.getByText("2 server(s)").first()).toBeVisible();
  await expect(page.getByText(/1 unhealthy/).first()).toBeVisible();

  await page.getByRole("button", { name: /push to 2 agent/i }).click();

  await expect.poll(() => puts, { timeout: 10_000 }).toBe(1);
  expect(refused).toBe(1);
  // The failure is surfaced per agent, not swallowed behind an all-clear.
  await expect(page.getByText(/read-only agent/)).toBeVisible();
  await expect(page.getByText(/pushed to 1 of 2 agents/i)).toBeVisible();
});

test("a new skill can be created from a template", async ({ page }) => {
  await stubAgent(page);

  // The agent had no create path at all before Stage 3.4 — the editor only opened
  // existing skills, so this asserts POST /api/skills is now reachable from the UI.
  await page.route("**/api/skills", async (route) => {
    if (route.request().method() === "POST") {
      const body = route.request().postDataJSON() as Record<string, unknown>;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id: "new-1",
          name: body.name,
          description: body.description,
          phraseReceivers: body.phraseReceivers ?? [],
          version: 1,
          successRate: 0,
          totalUses: 0,
          createdAt: "2026-10-06T10:00:00Z",
        }),
      });
    }
    return route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
  });

  await page.route("**/api/skills/new-1", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        meta: {
          id: "new-1",
          name: "http-caller",
          description: "Calls an HTTP API and summarises the response.",
          phraseReceivers: ["call http", "fetch api"],
          version: 1,
          successRate: 0,
          totalUses: 0,
          createdAt: "2026-10-06T10:00:00Z",
        },
        descriptionMarkdown: "Calls an HTTP API and summarises the response.",
        prompt: "# HTTP call\n\nUse the `http` tool.",
      }),
    }),
  );

  await connect(page);
  await page.getByRole("button", { name: "Skills", exact: true }).click();

  await page.getByRole("button", { name: /new skill from template/i }).click();
  await expect(page.getByRole("heading", { name: /new skill/i })).toBeVisible();

  // All five roadmap templates are offered.
  for (const label of [
    "HTTP call",
    "Code execution",
    "A2A delegate",
    "File-based .NET",
    "Blank",
  ]) {
    await expect(page.getByText(label, { exact: true })).toBeVisible();
  }

  // Creating from one lands in the editor with the template's prompt loaded.
  await page.getByText("HTTP call", { exact: true }).click();
  await expect(page.getByText(/created skill http-caller/i)).toBeVisible();
  // The editor opens the newly created skill rather than leaving the operator on the list.
  await expect(page.getByRole("heading", { name: "http-caller", exact: true })).toBeVisible();
});

test("a skill can be packaged as .skillpkg from the editor", async ({ page }) => {
  await stubAgent(page);

  const skill = {
    id: "a1b2c3d4",
    name: "answerer",
    description: "answers things",
    phraseReceivers: ["answer"],
    version: 2,
    successRate: 0.9,
    totalUses: 12,
    createdAt: "2026-01-01T00:00:00Z",
  };
  await page.route("**/api/skills", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([skill]) }),
  );
  await page.route("**/api/skills/a1b2c3d4", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        meta: skill,
        descriptionMarkdown: "answers things",
        prompt: "Answer concisely.",
      }),
    }),
  );

  await connect(page);
  await page.getByRole("button", { name: "Skills", exact: true }).click();
  await page.getByRole("button", { name: "answerer" }).first().click();

  // Intercept the download so the assertion is on the bytes the browser received.
  const [download] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("button", { name: /download \.skillpkg/i }).click(),
  ]);

  expect(download.suggestedFilename()).toBe("a1b2c3d4-v2.skillpkg");

  const stream = await download.createReadStream();
  const chunks: Buffer[] = [];
  for await (const chunk of stream) chunks.push(Buffer.from(chunk));
  const zip = Buffer.concat(chunks);

  // ZIP local-header signature, and the three required entries the spec mandates.
  expect(zip.readUInt32LE(0)).toBe(0x04034b50);
  const text = zip.toString("latin1");
  expect(text).toContain("a1b2c3d4/skill.meta.json");
  expect(text).toContain("a1b2c3d4/skill.prompt.md");
  expect(text).toContain("a1b2c3d4/skill.description.md");
  expect(text).toContain("phrase_receivers");
});

test("a denied sandbox scan blocks the run until overridden", async ({ page }) => {
  await stubAgent(page);

  const skill = {
    id: "s1",
    name: "answerer",
    description: "answers things",
    phraseReceivers: ["answer"],
    version: 2,
    successRate: 0.9,
    totalUses: 12,
    createdAt: "2026-01-01T00:00:00Z",
  };
  await page.route("**/api/skills", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([skill]) }),
  );
  await page.route("**/api/skills/s1", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ meta: skill, descriptionMarkdown: "answers things", prompt: "p" }),
    }),
  );

  // Mirrors the agent's DangerousCodeScanner: it stops at the first match, so reasons
  // and lineNumbers are parallel single-element lists.
  await page.route("**/api/code/scan", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        allowed: false,
        reasons: ["`File.Delete` matched at line 42"],
        lineNumbers: [42],
      }),
    }),
  );

  let runs = 0;
  await page.route("**/api/code/run", (route) => {
    runs += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ runId: "r1" }),
    });
  });

  await connect(page);
  await page.getByRole("button", { name: "Skills", exact: true }).click();
  await page.getByRole("button", { name: "answerer" }).first().click();

  const runButton = page.getByRole("button", { name: /▶\s*Run/i });
  await page.getByLabel(/sandbox code/i).fill('File.Delete("x");');

  await runButton.click();

  // The scanner's verdict is shown with its line number, and nothing ran.
  const report = page.getByTestId("scan-report");
  await expect(report).toBeVisible();
  await expect(report.getByText(/line 42/i)).toBeVisible();
  await expect(report.getByText(/File\.Delete/)).toBeVisible();
  expect(runs).toBe(0);

  // An explicit override is required — not a silent retry.
  await page.getByRole("checkbox").check();
  await runButton.click();
  await expect.poll(() => runs, { timeout: 10_000 }).toBe(1);
});

test("LLM judge picks a winner and explains itself", async ({ page }) => {
  await stubAgent(page);

  let judgeSeen = "";
  let fanOut = 0;
  await page.route("**/api/chat", async (route) => {
    const body = route.request().postDataJSON() as { message?: string };
    const message = body?.message ?? "";

    if (message.includes("You are a judge")) {
      judgeSeen = message;
      // Prose around the JSON: the parser has to cope with what models actually emit.
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          answer:
            '```json\n{"best_index": 2, "rationale": "it cites the rollout window"}\n```',
          mode: "chat",
          confidence: "0.7",
          provider: "test",
          skill: null,
          toolUsed: null,
          proposeSkillForInput: null,
          proposeImproveSkillId: null,
          proposeImproveSkillName: null,
        }),
      });
    }

    fanOut += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        answer: fanOut === 1 ? "hold the release" : "ship after the window",
        mode: "chat",
        confidence: "0.5",
        provider: "test",
        skill: null,
        toolUsed: null,
        proposeSkillForInput: null,
        proposeImproveSkillId: null,
        proposeImproveSkillName: null,
      }),
    });
  });

  await connect(page);

  // Second agent, so the judge has two candidates to choose between.
  await page.getByRole("button", { name: "Agents", exact: true }).click();
  await page.getByRole("button", { name: /add connection/i }).click();
  await page.getByLabel(/^name/i).fill("beta-agent");
  await page.getByLabel(/base url/i).fill("http://localhost:8422");
  await page.getByLabel(/api key/i).fill("hc_contrib_TEST");
  await page.getByRole("button", { name: /^save$/i }).click();
  await expect(page.getByText(/connected: beta-agent/i)).toBeVisible();

  await page.getByRole("button", { name: "Consensus", exact: true }).click();
  await page.getByRole("button", { name: /^local-agent$/ }).click();
  await page.getByRole("button", { name: /^beta-agent$/ }).click();
  await page.getByPlaceholder(/ask every selected agent/i).fill("ship on Friday?");
  await page.getByRole("button", { name: /send to all/i }).click();

  await expect(page.getByText("hold the release")).toBeVisible();
  await expect(page.getByText("ship after the window")).toBeVisible();

  await page.getByRole("button", { name: /llm judge/i }).click();
  await page.getByLabel(/judge agent/i).selectOption({ label: "local-agent" });
  await page.getByRole("button", { name: /run judge/i }).click();

  // The judge was asked for the structured contract, not for prose.
  await expect.poll(() => judgeSeen).toContain("best_index");

  // The winner's text is surfaced and the rationale is shown.
  await expect(page.getByText("it cites the rollout window")).toBeVisible();
});

test("a finished consensus round is recorded and readable from history", async ({ page }) => {
  await stubAgent(page);

  // Agent-side history: the round is POSTed when it completes and read back on demand.
  const saved: Array<Record<string, unknown>> = [];
  const stored = [
    {
      id: "past-1",
      prompt: "ship on Friday?",
      selectedAgents: ["alpha"],
      responses: [
        { agentName: "alpha", connectionId: "c1", answer: "hold the release" },
        { agentName: "beta", connectionId: "c2", answer: "ship after the window" },
      ],
      aggregationMode: "llm-judge",
      result: "ship after the window",
      judgeRationale: "cites the rollout window",
      createdAt: "2026-10-06T09:00:00Z",
    },
  ];

  await page.route("**/api/consensus/sessions", async (route) => {
    if (route.request().method() === "POST") {
      const body = route.request().postDataJSON() as Record<string, unknown>;
      saved.push(body);
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ ...stored[0], ...body, id: "just-saved" }),
      });
    }
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ count: stored.length, items: stored }),
    });
  });

  await page.route("**/api/chat", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        answer: "hold the release",
        mode: "chat",
        confidence: "0.5",
        provider: "test",
        skill: null,
        toolUsed: null,
        proposeSkillForInput: null,
        proposeImproveSkillId: null,
        proposeImproveSkillName: null,
      }),
    }),
  );

  await connect(page);

  // Second agent so the round is a real consensus.
  await page.getByRole("button", { name: "Agents", exact: true }).click();
  await page.getByRole("button", { name: /add connection/i }).click();
  await page.getByLabel(/^name/i).fill("beta-agent");
  await page.getByLabel(/base url/i).fill("http://localhost:8422");
  await page.getByLabel(/api key/i).fill("hc_contrib_TEST");
  await page.getByRole("button", { name: /^save$/i }).click();
  await expect(page.getByText(/connected: beta-agent/i)).toBeVisible();

  await page.getByRole("button", { name: "Consensus", exact: true }).click();
  await page.getByRole("button", { name: /^local-agent$/ }).click();
  await page.getByRole("button", { name: /^beta-agent$/ }).click();
  await page.getByPlaceholder(/ask every selected agent/i).fill("ship on Friday?");
  await page.getByRole("button", { name: /send to all/i }).click();

  // The round is persisted when it finishes, not when the page is closed.
  await expect.poll(() => saved.length, { timeout: 10_000 }).toBe(1);
  expect(saved[0].prompt).toBe("ship on Friday?");
  expect((saved[0].responses as unknown[]).length).toBe(2);

  // History lists past rounds and opens one read-only.
  await page.getByRole("button", { name: /^history/i }).click();
  const detail = page.getByTestId("consensus-history-detail");
  await expect(page.getByText(/no past consensus rounds/i)).toBeHidden();

  await page.getByRole("button", { name: /ship on friday\?/i }).first().click();
  await expect(detail).toBeVisible();
  await expect(detail.getByText("hold the release")).toBeVisible();
  // The winning text appears twice — once as that agent's answer, once as the result.
  await expect(detail.getByText("ship after the window").first()).toBeVisible();
  await expect(detail.getByText(/cites the rollout window/)).toBeVisible();
});

test("the E2E suite is hermetic — no request escapes to a real agent", async ({ page }) => {
  // Regression guard. `stubAgent` claims the suite needs no running agent; this proves
  // it, because an unstubbed request is a hard failure rather than a silent network
  // attempt. Two endpoints were missed this way: ConfigView loads API keys on mount
  // and ConsensusView records a finished round, so merely *visiting* those views made
  // a real call that failed silently — or would have really persisted a record on a
  // machine with an agent on 8421.
  const escapes: string[] = [];

  // Registered FIRST: Playwright matches the most recently added handler first, so
  // registering this before stubAgent leaves the stubs in front and turns this into a
  // catch-all for requests nothing else handled.
  await page.route("**/*", async (route) => {
    const url = route.request().url();
    if (url.includes(":8421")) {
      escapes.push(`${route.request().method()} ${url}`);
    }
    return route.fallback();
  });

  await stubAgent(page);

  // Visit every view that talks to the agent on mount. No heading assertion: the
  // point is to trigger the requests, and several views do not title themselves
  // exactly like their nav label.
  await connect(page);
  for (const nav of ["Config", "Consensus", "Chat", "Mesh", "Tools", "Workflow", "Skills"]) {
    await page.getByRole("button", { name: nav, exact: true }).click();
    await expect(page.getByRole("button", { name: nav, exact: true })).toBeVisible();
  }
  // Let any fire-and-forget mount request settle before asserting.
  await page.waitForTimeout(500);

  expect(escapes, `unstubbed agent requests: ${escapes.join(", ")}`).toEqual([]);
});

test("chat view sends a message and renders the reply with metadata", async ({ page }) => {
  await stubAgent(page);
  await page.route("**/api/chat", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        answer: "42",
        mode: "skill",
        confidence: "high",
        provider: "test-provider",
        skill: { id: "s1", name: "answerer" },
        proposeSkillForInput: null,
        proposeImproveSkillId: null,
        proposeImproveSkillName: null,
      }),
    }),
  );

  await connect(page);

  await page.getByRole("button", { name: "Chat", exact: true }).click();
  await expect(page.getByRole("heading", { name: /chat with your agent/i })).toBeVisible();

  await page.getByPlaceholder(/message the agent/i).fill("what is the answer?");
  await page.getByRole("button", { name: /^send$/i }).click();

  // User turn and agent reply both render.
  await expect(page.getByText("what is the answer?")).toBeVisible();
  await expect(page.getByText("42")).toBeVisible();

  // Metadata badges come from the response, proving the DTO is wired through.
  await expect(page.getByText("test-provider")).toBeVisible();
  await expect(page.getByText("answerer")).toBeVisible();
  await expect(page.getByText(/confidence/i).first()).toBeVisible();
});

test("chat surfaces an agent failure instead of hanging", async ({ page }) => {
  await stubAgent(page);
  await page.route("**/api/chat", (route) =>
    route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ error: "llm unavailable" }) }),
  );

  await connect(page);
  await page.getByRole("button", { name: "Chat", exact: true }).click();
  await page.getByPlaceholder(/message the agent/i).fill("hello");
  await page.getByRole("button", { name: /^send$/i }).click();

  // Rendered both in the transcript turn and in a toast; assert the transcript.
  await expect(page.getByText(/llm unavailable/i).first()).toBeVisible();

  // The composer must recover: Send is disabled while the box is empty, so
  // typing must re-enable it rather than leaving the view stuck on "busy".
  const composer = page.getByPlaceholder(/message the agent/i);
  await expect(composer).toBeEnabled();
  await composer.fill("second attempt");
  await expect(page.getByRole("button", { name: /^send$/i })).toBeEnabled();
});

test("skills view lists skills and streams a sandbox run over SSE", async ({ page }) => {
  await stubAgent(page);

  // The run is preceded by a safety scan (Stage 3.5). The scan fails closed, so a test
  // that wants to reach /api/code/run must say the scan passed.
  await page.route("**/api/code/scan", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ allowed: true, reasons: [], lineNumbers: [] }),
    }),
  );
  const skill = {
    id: "s1",
    name: "answerer",
    description: "answers things",
    // The agent returns phraseReceivers — `trigger(s)` is legacy input only
    // (WebApiAdapter.SkillDto). The generated type caught this divergence.
    phraseReceivers: ["answer"],
    version: 2,
    successRate: 0.9,
    totalUses: 12,
    createdAt: "2026-01-01T00:00:00Z",
  };
  await page.route("**/api/skills", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([skill]),
    }),
  );
  await page.route("**/api/skills/s1", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        meta: skill,
        descriptionMarkdown: "answers things",
        prompt: "Answer concisely.",
      }),
    }),
  );

  let runId = "";
  await page.route("**/api/code/run", (route) => {
    runId = "run-test-1";
    return route.fulfill({
      status: 202,
      contentType: "application/json",
      body: JSON.stringify({ runId }),
    });
  });
  // SSE frames arrive as one chunk; the client parser must split on blank lines.
  await page.route("**/api/code/run/*/stream**", (route) =>
    route.fulfill({
      status: 200,
      headers: { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" },
      body:
        'event: start\ndata: {"executor":"dotnet-file-based","language":"csharp"}\n\n' +
        'event: result\ndata: {"exitCode":0,"stdout":"hello\\n","stderr":"","durationMs":42,"status":"ok","success":true}\n\n' +
        'event: done\ndata: {"runId":"run-test-1","status":"completed","result":{"exitCode":0,"stdout":"hello\\n","stderr":"","durationMs":42,"status":"ok","blockedPatterns":[],"sessionDir":null,"success":true}}\n\n',
    }),
  );

  await connect(page);
  await page.getByRole("button", { name: "Skills", exact: true }).click();

  await expect(page.getByRole("heading", { name: "answerer" })).toBeVisible();

  await page.getByRole("button", { name: "▶ Run" }).click();

  // Streamed output from the SSE frames reaches the console pane.
  await expect(page.getByText(/run started/i)).toBeVisible();
  // "exit 0" appears both in the console line and the summary paragraph.
  await expect(page.getByText(/exit 0/).first()).toBeVisible();
  expect(runId).toBe("run-test-1");
});

test("config view edits a merge patch and gates restart behind the system role", async ({ page }) => {
  await stubAgent(page);
  await page.route("**/api/config", async (route) => {
    if (route.request().method() === "PATCH") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ status: "patched", config: {} }),
      });
    }
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ config: { llm: { provider: "ollama" } }, source: "runtime" }),
    });
  });
  // Contribute sessions must not see the restart button.
  await page.route("**/api/system/restart", (route) =>
    route.fulfill({ status: 403, contentType: "application/json", body: JSON.stringify({ error: "system role required" }) }),
  );

  await connect(page);
  await page.getByRole("button", { name: "Config", exact: true }).click();

  await expect(page.getByRole("heading", { name: /configuration/i })).toBeVisible();
  const editor = page.locator("textarea").first();
  // v-model writes the textarea's value, not its textContent.
  await expect(editor).toHaveValue(/ollama/);

  // Invalid JSON blocks the save button rather than shipping a bad patch.
  await editor.fill("{ not json");
  await expect(page.getByText(/invalid json|not a valid json/i)).toBeVisible();
  await expect(page.getByRole("button", { name: /save patch/i })).toBeDisabled();

  // A valid object re-enables saving.
  await editor.fill(JSON.stringify({ llm: { provider: "yandex" } }, null, 2));
  await expect(page.getByRole("button", { name: /save patch/i })).toBeEnabled();
  await page.getByRole("button", { name: /save patch/i }).click();
  await expect(page.getByText(/configuration updated/i)).toBeVisible();

  // Contribute role: restart is explained, not offered.
  await expect(page.getByText(/requires a system-role session/i)).toBeVisible();
  await expect(page.getByRole("button", { name: /request restart/i })).toHaveCount(0);
});

test("roles editor manages API keys without ever exposing them", async ({ page }) => {
  await stubAgent(page);

  // Re-registering the exchange last takes precedence, so this session is a system one.
  await page.route("**/api/studio/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        token: "test-system-token",
        role: "system",
        agentId: "hercules-main",
        displayName: "Hercules",
        expiresAt: new Date(Date.now() + 1_800_000).toISOString(),
        capabilities: ["chat", "config:write", "system:restart"],
        ttlSeconds: 1800,
      }),
    }),
  );

  await page.route("**/api/config", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ config: {}, source: "runtime" }),
    }),
  );

  await page.route("**/api/auth/keys", async (route) => {
    if (route.request().method() === "POST") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          key: { fingerprint: "aaaabbbbcccc", role: "contribute", description: "CI", label: "hc_contrib_" },
          generatedKey: "hc_contrib_ONCE_ONLY_SECRET",
        }),
      });
    }
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        count: 2,
        contributeCount: 1,
        systemCount: 1,
        currentFingerprint: "aaaabbbbcccc",
        keys: [
          { fingerprint: "aaaabbbbcccc", role: "contribute", description: "CI", label: "hc_contrib_" },
          { fingerprint: "111122223333", role: "system", description: null, label: "hc_sys_" },
        ],
      }),
    });
  });

  const patches: string[] = [];
  await page.route("**/api/auth/keys/*", async (route) => {
    patches.push(route.request().method());
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ fingerprint: "aaaabbbbcccc", systemCount: 1, revokedSessions: 2 }),
    });
  });

  await connect(page);
  await page.getByRole("button", { name: "Config", exact: true }).click();

  // System role sees the section; contribute sessions do not (covered by the
  // restart-gating test above, which asserts the contribute view of this page).
  await expect(page.getByRole("heading", { name: /API keys & roles/i })).toBeVisible();

  // Rows are addressed by fingerprint — the plaintext key is not in the list payload.
  await expect(page.getByText("aaaabbbbcccc")).toBeVisible();
  await expect(page.getByText("111122223333")).toBeVisible();
  await expect(page.getByText("this session")).toBeVisible();

  // Creating a key reveals the generated plaintext exactly once.
  await page.getByRole("button", { name: /generate key/i }).click();
  await expect(page.getByText("hc_contrib_ONCE_ONLY_SECRET")).toBeVisible();
  await page.getByRole("button", { name: /^close$/i }).click();
  await expect(page.getByText("hc_contrib_ONCE_ONLY_SECRET")).toHaveCount(0);

  // Demotion goes to the fingerprint-addressed endpoint, not the config patch.
  await page.getByLabel(/role for key 111122223333/i).selectOption("contribute");
  await expect.poll(() => patches).toEqual(["PATCH"]);
});

test("session store editor stages a backend change without clobbering the secret", async ({ page }) => {
  await stubAgent(page);

  // System-role session, because this section configures where data is persisted.
  await page.route("**/api/studio/session", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        token: "test-system-token",
        role: "system",
        agentId: "hercules-main",
        displayName: "Hercules",
        expiresAt: new Date(Date.now() + 1_800_000).toISOString(),
        capabilities: ["chat", "config:write"],
        ttlSeconds: 1800,
      }),
    }),
  );

  // Mirrors the agent's redaction: connectionString comes back as the marker.
  await page.route("**/api/config", async (route) => {
    if (route.request().method() === "PATCH") {
      patched.push(JSON.parse(route.request().postData() ?? "{}"));
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ status: "patched", config: {} }),
      });
    }
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        source: "file",
        config: {
          storage: {
            sessionStore: {
              provider: "sqlite",
              connectionString: "__hercules_redacted__",
              schema: "public",
            },
          },
        },
      }),
    });
  });

  const patched: Array<Record<string, unknown>> = [];

  await connect(page);
  await page.getByRole("button", { name: "Config", exact: true }).click();

  await expect(page.getByRole("heading", { name: /session store backend/i })).toBeVisible();

  // The current provider is read back; the secret never is.
  await expect(page.getByLabel(/^provider$/i)).toHaveValue("sqlite");
  await expect(page.getByLabel(/connection string/i)).toHaveValue("");

  // Both warnings are visible: restart semantics and the partial backend.
  await expect(page.getByText(/after a restart/i)).toBeVisible();
  await expect(page.getByText(/partially implemented/i)).toBeVisible();

  // Switching provider alone must not send a connection string — the existing
  // secret has to survive untouched.
  await page.getByLabel(/^provider$/i).selectOption("postgres");
  await page.getByRole("button", { name: /save backend/i }).click();

  await expect.poll(() => patched.length).toBe(1);
  const sent = (patched[0] as { storage: { sessionStore: Record<string, unknown> } }).storage
    .sessionStore;
  expect(sent.provider).toBe("postgres");
  expect(sent.connectionString).toBeUndefined();

  // Saving re-reads the server, which still reports sqlite — so the fields disable
  // again. That round trip is deliberate: the view trusts the server, not the draft.
  await expect(page.getByLabel(/^provider$/i)).toHaveValue("sqlite");

  // Typing a connection string explicitly does send it.
  await page.getByLabel(/^provider$/i).selectOption("postgres");
  await page.getByLabel(/connection string/i).fill("Host=db.internal;Password=x");
  await page.getByRole("button", { name: /save backend/i }).click();

  await expect.poll(() => patched.length).toBe(2);
  expect(
    (patched[1] as { storage: { sessionStore: Record<string, unknown> } }).storage.sessionStore
      .connectionString,
  ).toBe("Host=db.internal;Password=x");
});

test("mesh context menu offers register and cleanup", async ({ page }) => {
  await stubAgent(page);

  // The canvas draws nodes from dashboard.topology, so the stub needs one or there
  // is nothing to right-click.
  await page.route("**/api/mesh/dashboard", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        topology: {
          agentCount: 1,
          agents: [
            {
              agentId: "agent-1",
              displayName: "Alpha",
              endpoint: "http://127.0.0.1:9001",
              healthScore: 0.9,
              latencyMs: 12,
              qualityScore: 0.8,
              trustLevel: "trusted",
              lastSeen: null,
              capabilities: ["chat"],
            },
          ],
          generatedAt: "2026-10-06T09:00:00Z",
        },
      }),
    }),
  );

  let cleaned = 0;
  await page.route("**/api/mesh/agents/cleanup", (route) => {
    cleaned += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ status: "cleaned", removedCount: 3 }),
    });
  });

  await connect(page);
  await page.getByRole("button", { name: "Mesh", exact: true }).click();

  await page.locator(".vue-flow__node").first().click({ button: "right" });
  const menu = page.getByRole("menu");

  // 4.4 lists six actions; the registry-wide two have no target node.
  await expect(menu.getByRole("menuitem")).toHaveCount(6);
  await expect(menu.getByRole("menuitem", { name: /register peer/i })).toBeVisible();
  await expect(menu.getByRole("menuitem", { name: /cleanup stale/i })).toBeVisible();

  page.once("dialog", (d) => d.accept());
  await menu.getByRole("menuitem", { name: /cleanup stale/i }).click();
  await expect.poll(() => cleaned).toBe(1);
  await expect(page.getByText(/removed 3 stale agent/i)).toBeVisible();
});

test("router explorer sorts and filters candidates", async ({ page }) => {
  await stubAgent(page);

  // Ordered deliberately against composite score so a sort change is observable.
  let routeCalls = 0;
  await page.route("**/api/mesh/**", (route) => {
    routeCalls += 1;
    const url = route.request().url();
    if (!url.includes("/routes")) return route.fallback();
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        capability: "deploy",
        count: 4,
        candidates: [
          {
            agentId: "a-best",
            displayName: "A-best",
            endpoint: "http://a",
            healthScore: 0.5,
            latencyMs: 900,
            qualityScore: 0.5,
            trustLevel: "trusted",
            compositeScore: 0.9,
            costHintUsd: 0.05,
            circuitState: "Closed",
            lastSeen: null,
            trustPassed: true,
          },
          {
            agentId: "b-healthy",
            displayName: "B-healthy",
            endpoint: "http://b",
            healthScore: 0.95,
            latencyMs: 40,
            qualityScore: 0.9,
            trustLevel: "trusted",
            compositeScore: 0.6,
            costHintUsd: 0.02,
            circuitState: "Closed",
            lastSeen: null,
            trustPassed: true,
          },
          {
            agentId: "c-untrusted",
            displayName: "C-untrusted",
            endpoint: "http://c",
            healthScore: 0.99,
            latencyMs: 10,
            qualityScore: 0.9,
            trustLevel: "unknown",
            compositeScore: 0.8,
            costHintUsd: 0.01,
            circuitState: "Closed",
            lastSeen: null,
            trustPassed: false,
          },
          {
            agentId: "d-open",
            displayName: "D-open",
            endpoint: "http://d",
            // Health deliberately below B-healthy so the ordering after each filter
            // is unambiguous rather than a tie-break on equal scores.
            healthScore: 0.85,
            latencyMs: 10,
            qualityScore: 0.9,
            trustLevel: "trusted",
            compositeScore: 0.7,
            costHintUsd: 0.01,
            circuitState: "Open",
            lastSeen: null,
            trustPassed: true,
          },
        ],
      }),
    });
  });

  await connect(page);
  await page.getByRole("button", { name: "Mesh", exact: true }).click();

  await page.getByLabel(/capability/i).first().fill("deploy");
  const inspectBtn = page.getByRole("button", { name: /^inspect$/i });
  await expect(inspectBtn).toBeEnabled();
  await inspectBtn.click();

  const rows = page.getByTestId("route-row");
  await expect(rows, `route stub hit ${routeCalls}x`).toHaveCount(4);

  // Default order is composite score: A-best (0.9) first.
  await expect(rows.first()).toContainText("A-best");

  // Sorting by health puts C-untrusted (0.99) first — above A-best (0.5).
  await page.getByLabel(/^sort$/i).selectOption("health");
  await expect(rows.first()).toContainText("C-untrusted");

  // Trust filter drops it, leaving B-healthy (0.95) on top.
  await page.getByRole("checkbox", { name: /trust passed only/i }).check();
  await expect(rows).toHaveCount(3);
  await expect(page.getByText(/C-untrusted/)).toHaveCount(0);
  await expect(rows.first()).toContainText("B-healthy");

  // Circuit filter drops D-open, leaving two candidates.
  await page.getByRole("checkbox", { name: /circuit not open/i }).check();
  await expect(rows).toHaveCount(2);
  await expect(page.getByText(/D-open/)).toHaveCount(0);
  await expect(rows.first()).toContainText("B-healthy");
});

test("mesh dashboard panels render traffic, heatmap and eval summary", async ({ page }) => {
  await stubAgent(page);

  // The topology canvas already fetched this payload and discarded everything but
  // `topology`; 4.7's panels render the rest of it.
  await page.route("**/api/mesh/dashboard", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        topology: { agentCount: 0, agents: [], generatedAt: "2026-10-06T09:00:00Z" },
        traffic: {
          totalRequests: 120,
          fanOutRequests: 30,
          meshDelegations: 12,
          avgLatencyMs: 41.5,
          meshRouterHits: 9,
          circuitBreakerRejections: 2,
          from: "2026-10-06T08:00:00Z",
          to: "2026-10-06T09:00:00Z",
        },
        health: { agents: [], healthyCount: 0, degradedCount: 0, unhealthyCount: 0 },
        skillHeatmap: {
          totalSkills: 2,
          skills: [
            {
              skillId: "s1",
              skillName: "answerer",
              totalUses: 90,
              successRate: 0.9,
              version: 2,
              createdAt: "2026-01-01T00:00:00Z",
            },
            {
              skillId: "s2",
              skillName: "deployer",
              totalUses: 10,
              successRate: 0.5,
              version: 1,
              createdAt: "2026-01-01T00:00:00Z",
            },
          ],
        },
        evalSummary: {
          totalRuns: 10,
          passedRuns: 8,
          failedRuns: 2,
          recentRuns: [
            {
              runId: "r1",
              scenarioType: "mesh-route",
              passed: true,
              score: 0.91,
              successRate: 0.9,
              assertionsPassed: 5,
              assertionsFailed: 0,
              startTimeUtc: "2026-10-06T08:30:00Z",
              durationMs: 1200,
            },
          ],
        },
        generatedAt: "2026-10-06T09:00:00Z",
      }),
    }),
  );

  await connect(page);
  await page.getByRole("button", { name: "Mesh", exact: true }).click();

  await expect(page.getByRole("heading", { name: "Traffic", exact: true })).toBeVisible();
  await expect(page.getByText("120", { exact: true })).toBeVisible();
  await expect(page.getByText("41.5 ms")).toBeVisible();

  // Non-exact: the heatmap heading also carries the skill count in a nested span, so
  // its accessible name is "Skill usage 2 skill(s)".
  await expect(page.getByRole("heading", { name: /Skill usage/ })).toBeVisible();
  await expect(page.getByText("answerer", { exact: true })).toBeVisible();
  await expect(page.getByText("90", { exact: true })).toBeVisible();

  await expect(page.getByRole("heading", { name: "Evaluations", exact: true })).toBeVisible();
  await expect(page.getByText("mesh-route", { exact: true })).toBeVisible();
});

test("mesh view auto-refreshes every 30s and stops when the view unmounts", async ({ page }) => {
  await stubAgent(page);

  let agentCalls = 0;
  await page.route("**/api/mesh/agents", (route) => {
    agentCalls += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ count: 0, agents: [] }),
    });
  });

  await connect(page);

  // The clock must be installed *before* the view mounts, or the interval it
  // registers is a real one and fast-forward never reaches it.
  await page.clock.install();

  await page.getByRole("button", { name: "Mesh", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Mesh", exact: true })).toBeVisible();
  const initial = agentCalls;
  expect(initial).toBeGreaterThan(0);

  // Stage 4.1 — the 30s auto-refresh, without waiting 30 real seconds.
  await page.clock.fastForward("00:00:35");
  await expect.poll(() => agentCalls).toBeGreaterThan(initial);

  // Leaving the view must stop the timer, or switching away leaves polling running.
  await page.getByRole("button", { name: "Chat", exact: true }).click();
  const afterLeaving = agentCalls;
  await page.clock.fastForward("02:00");
  expect(agentCalls).toBe(afterLeaving);
});

test("mesh view renders registry health and degrades gracefully", async ({ page }) => {
  await stubAgent(page);
  await page.route("**/api/mesh/agents", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        count: 2,
        agents: [
          {
            agentId: "agent-1",
            displayName: "Alpha",
            endpoint: "http://127.0.0.1:9001",
            trustLevel: "trusted",
            healthScore: 0.9,
            latencyMs: 12,
          },
          {
            agentId: "agent-2",
            displayName: "Beta",
            endpoint: "http://127.0.0.1:9002",
            trustLevel: "unknown",
            healthScore: 0.3,
            latencyMs: 240,
          },
        ],
      }),
    }),
  );
  await page.route("**/api/mesh/dashboard", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        topology: {
          agentCount: 2,
          agents: [
            {
              agentId: "agent-1",
              displayName: "Alpha",
              endpoint: "http://127.0.0.1:9001",
              healthScore: 0.9,
              latencyMs: 12,
              qualityScore: 0.8,
              trustLevel: "trusted",
              lastSeen: null,
              capabilities: ["chat", "deploy"],
            },
            {
              agentId: "agent-2",
              displayName: "Beta",
              endpoint: "http://127.0.0.1:9002",
              healthScore: 0.3,
              latencyMs: 240,
              qualityScore: 0.4,
              trustLevel: "unknown",
              lastSeen: null,
              capabilities: ["chat"],
            },
          ],
          generatedAt: "2026-10-06T09:00:00Z",
        },
        health: { agents: [], healthyCount: 0, degradedCount: 0, unhealthyCount: 0 },
      }),
    }),
  );

  await page.route("**/api/mesh/health", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        agents: [
          {
            agentId: "agent-1",
            displayName: "Alpha",
            healthScore: 0.9,
            healthStatus: "healthy",
            circuitState: "closed",
            consecutiveFailures: 0,
            lastSeen: null,
            avgLatencyMs: 11,
          },
        ],
        healthyCount: 1,
        degradedCount: 1,
        unhealthyCount: 0,
      }),
    }),
  );
  // Denials failing must not blank the rest of the view.
  await page.route("**/api/mesh/denials**", (route) =>
    route.fulfill({ status: 500, contentType: "application/json", body: "{}" }),
  );

  // Stage 4.6 / 4.7 — shared memory browser + circuit breakers.
  // `/api/mesh/circuits` answers a MAP (CircuitBreaker.GetAllStates), not an array.
  await page.route("**/api/mesh/circuits", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ "agent-2": "Open", "agent-1": "Closed" }),
    }),
  );

  let resetCalls = 0;
  await page.route("**/api/mesh/circuits/*/reset", (route) => {
    resetCalls += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ status: "reset" }),
    });
  });

  await page.route("**/api/mesh/shared-memory", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        {
          id: "fact-1",
          category: "deploy",
          content: "Friday releases are frozen",
          allowedAgents: ["agent-1"],
          publishedAt: "2026-10-06T09:00:00Z",
        },
      ]),
    }),
  );

  await connect(page);
  await page.getByRole("button", { name: "Mesh", exact: true }).click();

  // Stage 4.2 — the topology canvas renders nodes from the typed dashboard.
  await expect(page.getByRole("heading", { name: "Topology", exact: true })).toBeVisible();
  // Scoped to the canvas: the health table below renders the same agent names.
  const canvas = page.locator(".vue-flow");
  await expect(canvas.getByText("Alpha", { exact: true })).toBeVisible();
  await expect(canvas.getByText("Beta", { exact: true })).toBeVisible();

  // Stage 4.7 — the circuits map is expanded into rows (an array check would empty it).
  const circuits = page.getByRole("heading", { name: "Circuit breakers", exact: true });
  await expect(circuits).toBeVisible();
  await expect(page.getByText("1 open")).toBeVisible();

  // Stage 4.6 — shared facts list, with the audience shown per row.
  await expect(page.getByRole("heading", { name: "Shared memory", exact: true })).toBeVisible();
  await expect(page.getByText("Friday releases are frozen")).toBeVisible();
  await expect(page.getByText("deploy", { exact: true })).toBeVisible();

  // Stage 4.4 — right-clicking a node opens the action menu.
  let touchCalls = 0;
  await page.route("**/api/mesh/agents/*/touch", (route) => {
    touchCalls += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ status: "touched" }),
    });
  });

  await canvas.locator(".vue-flow__node").first().click({ button: "right" });
  const menu = page.getByRole("menu");
  await expect(menu).toBeVisible();
  // 4.4 specifies six actions; the two registry-wide ones were added after this test
  // first pinned the count at four.
  await expect(menu.getByRole("menuitem")).toHaveCount(6);

  // Touch is a real POST to the agent id of the right-clicked node.
  await menu.getByRole("menuitem", { name: /touch/i }).click();
  await expect.poll(() => touchCalls).toBe(1);
  await expect(menu).toHaveCount(0);

  // Circuit reset from the menu also round-trips (first reset of this test).
  await canvas.locator(".vue-flow__node").first().click({ button: "right" });
  await page.getByRole("menu").getByRole("menuitem", { name: /reset circuit/i }).click();
  await expect.poll(() => resetCalls).toBe(1);
  // The legend explains the colour and size encoding rather than leaving it implicit.
  await expect(page.getByText(/node size = capability count/i)).toBeVisible();
  await expect(page.getByText(/does not expose trust\/delegation relations as edges/i)).toBeVisible();

  await expect(page.getByRole("heading", { name: "Mesh", exact: true })).toBeVisible();
  // Scoped to the health table: the Stage 4 canvas above renders the same agent names.
  const healthTable = page.getByRole("table");
  await expect(healthTable.getByText("Alpha")).toBeVisible();
  await expect(healthTable.getByText("Beta")).toBeVisible();

  // Summary counters come from /api/mesh/health. Scoped to the table — the circuit
  // panel renders the same state words.
  await expect(healthTable.getByText("closed")).toBeVisible();

  // Filtering narrows the table. Scoped to the table: the canvas above is not filtered
  // by the table's search box, so Beta still exists there.
  await page.getByPlaceholder(/search/i).fill("alpha");
  await expect(healthTable.getByText("Beta")).toHaveCount(0);
  await expect(healthTable.getByText("Alpha")).toBeVisible();
});

test("tools view toggles a tool and reverts when the agent rejects it", async ({ page }) => {
  await stubAgent(page);

  const tool = {
    name: "http-tool",
    category: "Network",
    description: "Performs HTTP requests",
    enabled: false,
    allowed: false,
    registeredAt: "2026-01-01T00:00:00Z",
    source: "di",
    supportsHealthCheck: true,
    healthStatus: "Healthy",
    lastCheckedAt: null,
    lastError: null,
    consecutiveFailures: 0,
    sideEffectLevel: "High",
    requiredPermissions: null,
    timeoutSeconds: 30,
    limits: { maxCallsPerMinute: 60, timeoutSeconds: 30 },
  };
  await page.route("**/api/tools", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ count: 1, allowedCount: 0, tools: [tool] }),
    }),
  );

  let enableStatus = 200;
  await page.route("**/api/tools/http-tool/enable", (route) =>
    route.fulfill({
      status: enableStatus,
      contentType: "application/json",
      body: JSON.stringify(enableStatus === 200 ? { ...tool, enabled: true } : { error: "tool policy forbids this" }),
    }),
  );

  await connect(page);
  await page.getByRole("button", { name: "Tools", exact: true }).click();

  await expect(page.getByRole("heading", { name: "Tools", exact: true })).toBeVisible();
  const toggle = page.getByRole("button", { name: /enable http-tool/i });
  await expect(toggle).toBeVisible();

  // Successful enable: the control flips to the disable affordance.
  await toggle.click();
  await expect(page.getByRole("button", { name: /disable http-tool/i })).toBeVisible();

  // Rejected disable must revert the switch instead of silently lying.
  enableStatus = 500;
  await page.route("**/api/tools/http-tool/disable", (route) =>
    route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ error: "locked by policy" }) }),
  );
  await page.getByRole("button", { name: /disable http-tool/i }).click();

  await expect(page.getByText(/could not change the tool state/i)).toBeVisible();
  // Back to "enabled" — the optimistic update was rolled back.
  await expect(page.getByRole("button", { name: /disable http-tool/i })).toBeVisible();
});

test("tools view edits MCP servers and reloads them", async ({ page }) => {
  await stubAgent(page);

  await page.route("**/api/tools", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ count: 0, allowedCount: 0, tools: [] }),
    }),
  );

  // Stage 5b: every row carries its live `config` so the editor can round-trip it
  // through PATCH /api/config. The view reads config.enabled, so a stub without it
  // would throw rather than merely look wrong.
  const serverFixture = () => ({
    count: 2,
    servers: [
      {
        name: "filesystem",
        transport: "stdio",
        status: "Healthy",
        toolCount: 7,
        connectedAt: "2026-10-06T09:00:00Z",
        error: null,
        config: {
          name: "filesystem",
          transport: "stdio",
          command: "npx",
          args: ["-y", "@modelcontextprotocol/server-filesystem"],
          endpoint: null,
          enabled: true,
          healthCheckEnabled: true,
          timeoutSeconds: 30,
        },
      },
      {
        name: "search",
        transport: "http",
        status: "Unhealthy",
        toolCount: 0,
        connectedAt: null,
        error: "connection refused",
        config: {
          name: "search",
          transport: "http",
          command: null,
          args: [],
          endpoint: "https://search.test/mcp",
          enabled: false,
          healthCheckEnabled: true,
          timeoutSeconds: 30,
        },
      },
    ],
  });

  await page.route("**/api/mcp/servers", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(serverFixture()),
    }),
  );

  let reloads = 0;
  await page.route("**/api/mcp/servers/reload", (route) => {
    reloads += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ message: "MCP connections reloaded" }),
    });
  });

  // The whole `mcp.servers` array is written, because a merge patch replaces
  // arrays wholesale rather than merging them by index.
  const patches: Array<{ mcp: { servers: Array<{ name: string; enabled: boolean }> } }> = [];
  await page.route("**/api/config", (route) => {
    if (route.request().method() !== "PATCH") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ config: {}, source: "runtime" }),
      });
    }
    patches.push(JSON.parse(route.request().postData() ?? "{}"));
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ status: "patched", config: {} }),
    });
  });

  await connect(page);
  await page.getByRole("button", { name: "Tools", exact: true }).click();

  await expect(page.getByRole("heading", { name: /MCP servers/i })).toBeVisible();
  // Exact match: the row also previews its command/args, which contain "filesystem".
  await expect(page.getByText("filesystem", { exact: true })).toBeVisible();
  await expect(page.getByText("search", { exact: true })).toBeVisible();
  await expect(page.getByText("connection refused")).toBeVisible();

  // The disabled server is labelled as such rather than shown as merely unhealthy.
  await expect(page.getByText("off", { exact: true })).toBeVisible();

  // Toggling writes the complete server list, not a single-key patch, and flips
  // exactly the row whose switch was pressed.
  await page.getByRole("button", { name: /enable search/i }).click();
  await expect.poll(() => patches.length).toBe(1);
  expect(patches[0].mcp.servers.map((s) => `${s.name}:${s.enabled}`)).toEqual([
    "filesystem:true",
    "search:true",
  ]);
  await expect(page.getByText(/MCP server saved/i)).toBeVisible();

  // Writing the config also awaits an explicit reload, so the status shown
  // afterwards is settled rather than caught mid-reconnect.
  await expect.poll(() => reloads).toBe(1);

  // The manual reload button adds exactly one more.
  await page.getByRole("button", { name: /^reload$/i }).click();
  await expect(page.getByText(/MCP connections reloaded/i)).toBeVisible();
  await expect.poll(() => reloads).toBe(2);
});

test("decisions view queues human decisions and clears them after approval", async ({ page }) => {
  await stubAgent(page);

  let approvals: unknown[] = [
    {
      requestId: "req-1",
      sessionId: "s1",
      toolName: "http-tool",
      argumentsJson: '{"url":"https://example.com"}',
      reason: "agent wants to call an external service",
      requestedAt: "2026-10-06T10:00:00Z",
      status: "Pending",
    },
  ];
  await page.route("**/api/approvals/pending", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ count: approvals.length, approvals }),
    }),
  );
  // Approving removes it from the agent's pending queue.
  await page.route("**/api/approvals/req-1/approve", (route) => {
    approvals = [];
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ message: "approved" }),
    });
  });

  let escalations: unknown[] = [
    {
      escalationId: "esc-1",
      requestId: "req-2",
      sessionId: "s1",
      type: "ToolIntent",
      severity: "high",
      status: "Pending",
      actionPlan: "fall back to the cached copy",
      context: "upstream unavailable",
      payloadJson: "",
      toolOrIntentName: "web-search",
      requestedBy: "agent-alpha",
      createdAt: "2026-10-06T10:05:00Z",
      resolvedAt: null,
      resolvedBy: null,
    },
  ];
  await page.route("**/api/escalations/pending**", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ count: escalations.length, escalations }),
    }),
  );
  await page.route("**/api/escalations/esc-1/approve", (route) => {
    escalations = [];
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ status: "approved" }),
    });
  });

  await connect(page);
  await page.getByRole("button", { name: "Decisions", exact: true }).click();

  await expect(page.getByRole("heading", { name: "Decisions", exact: true })).toBeVisible();
  await expect(page.getByText("http-tool")).toBeVisible();
  await expect(page.getByText("web-search")).toBeVisible();
  await expect(page.getByText(/fall back to the cached copy/i)).toBeVisible();
  // "agent-alpha" is unambiguous; "high" would also match the severity <option>.
  await expect(page.getByText(/agent-alpha/)).toBeVisible();

  // Approving an approval re-reads the queue, so the row disappears.
  await page.getByRole("button", { name: /^confirm http-tool$/i }).click();
  await expect(page.getByText("http-tool")).toHaveCount(0);
  // The unrelated escalation is untouched.
  await expect(page.getByText("web-search")).toBeVisible();

  await page.getByRole("button", { name: /^confirm web-search$/i }).click();
  await expect(page.getByText("web-search")).toHaveCount(0);
  await expect(page.getByText(/nothing is waiting for a decision/i)).toBeVisible();
});

test("consensus fans out to several agents and aggregates without losing failures", async ({ page }) => {
  await stubAgent(page);

  // Two agents, distinguishable by base URL. `failSecond` lets one test prove that a
  // single unreachable agent does not discard the others' answers.
  let failSecond = false;
  await page.route("**/api/chat", async (route) => {
    const url = route.request().url();
    const isSecond = url.includes("8422");

    if (isSecond && failSecond) {
      return route.fulfill({
        status: 500,
        contentType: "application/json",
        body: JSON.stringify({ error: "agent unavailable" }),
      });
    }

    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        answer: isSecond ? "Roll the change forward." : "Hold and wait for confirmation.",
        mode: "chat",
        confidence: isSecond ? 0.9 : 0.4,
        provider: isSecond ? "ollama-local" : "yandexgpt",
        skill: null,
        toolUsed: null,
        proposeSkillForInput: null,
        proposeImproveSkillId: null,
        proposeImproveSkillName: null,
      }),
    });
  });

  await page.goto("./");
  await page.getByRole("button", { name: /accept/i }).click();

  // First agent comes from the first-run empty state...
  await page.getByRole("button", { name: /add connection manually/i }).click();
  await page.getByLabel(/^name/i).fill("alpha-agent");
  await page.getByLabel(/base url/i).fill("http://localhost:8421");
  await page.getByLabel(/api key/i).fill("hc_contrib_TEST");
  await page.getByRole("button", { name: /^save$/i }).click();
  await expect(page.getByText(/connected: alpha-agent/i)).toBeVisible();

  // ...the second from the Agents view, which is where the add affordance lives
  // once the app is no longer in its first-run state.
  await page.getByRole("button", { name: "Agents", exact: true }).click();
  await page.getByRole("button", { name: /add connection/i }).click();
  await page.getByLabel(/^name/i).fill("beta-agent");
  await page.getByLabel(/base url/i).fill("http://localhost:8422");
  await page.getByLabel(/api key/i).fill("hc_contrib_TEST");
  await page.getByRole("button", { name: /^save$/i }).click();
  await expect(page.getByText(/connected: beta-agent/i)).toBeVisible();

  await page.getByRole("button", { name: "Consensus", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Consensus", exact: true })).toBeVisible();
  await expect(page.getByText(/Select at least two agents/i)).toBeVisible();

  const sendButton = page.getByRole("button", { name: /send to all/i });
  await expect(sendButton).toBeDisabled();

  // One agent is not a consensus, and the view says so rather than just disabling.
  await page.getByRole("button", { name: "alpha-agent" }).click();
  await expect(page.getByText(/needs at least two agents/i)).toBeVisible();
  await expect(sendButton).toBeDisabled();

  await page.getByRole("button", { name: "beta-agent" }).click();
  await page.getByPlaceholder(/Ask every selected agent/i).fill("Should we ship on Friday?");
  await expect(sendButton).toBeEnabled();
  await sendButton.click();

  // Both answers arrive side by side.
  await expect(page.getByText("Hold and wait for confirmation.")).toBeVisible();
  await expect(page.getByText("Roll the change forward.")).toBeVisible();

  // Manual aggregation picks a column rather than inventing a merge.
  await page.getByRole("button", { name: /^Best$/ }).first().click();
  await expect(page.getByText("Your pick")).toBeVisible();

  // A failing agent degrades to one column, it does not blank the round.
  failSecond = true;
  await page.getByRole("button", { name: /new round/i }).click();
  await sendButton.click();

  await expect(page.getByText("Hold and wait for confirmation.")).toBeVisible();
  await expect(page.getByText(/agent unavailable/)).toBeVisible();
  await expect(page.getByText(/aggregation uses what arrived/i)).toBeVisible();
});

test("workflow editor creates a definition and updates it in place", async ({ page }) => {
  await stubAgent(page);

  // Server-side model: POST without an id creates, with an id updates in place.
  const definitions: Array<Record<string, unknown>> = [];
  let nextId = 1;
  const saved: Array<Record<string, unknown>> = [];

  await page.route("**/api/workflows?**", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: definitions.map(({ graphJson: _g, ...rest }) => rest),
        nextCursor: null,
        hasMore: false,
      }),
    }),
  );

  await page.route("**/api/workflows", async (route) => {
    if (route.request().method() !== "POST") return route.fallback();
    const body = JSON.parse(route.request().postData() ?? "{}") as Record<string, unknown>;
    saved.push(body);

    const id = typeof body.id === "string" ? body.id : `wf-${nextId++}`;
    const record = {
      id,
      name: body.name,
      version: body.version,
      description: body.description ?? null,
      createdAt: "2026-10-06T09:00:00Z",
      graphJson: body.graphJson,
    };
    const at = definitions.findIndex((d) => d.id === id);
    if (at >= 0) definitions[at] = record;
    else definitions.push(record);

    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(record),
    });
  });

  await page.route("**/api/workflows/*", async (route) => {
    const id = route.request().url().split("/").pop() ?? "";
    const found = definitions.find((d) => d.id === id);
    return route.fulfill({
      status: found ? 200 : 404,
      contentType: "application/json",
      body: JSON.stringify(found ?? { error: "not found" }),
    });
  });

  await page.goto("./");
  await page.getByRole("button", { name: /accept/i }).click();
  await page.getByRole("button", { name: "Workflow", exact: true }).click();

  await page.getByLabel(/client id/i).fill("wf-client");
  await page.getByLabel(/client secret/i).fill("wf-secret");
  await page.getByRole("button", { name: /^connect to workflow server$/i }).click();

  // --- Create ---
  await page.getByRole("button", { name: /new workflow/i }).click();
  await page.getByLabel(/^name$/i).first().fill("release-pipeline");
  await page.getByRole("button", { name: "+ ServiceTaskNode" }).click();

  // A service task with no intent is incomplete, so saving must stay blocked.
  const saveButton = page.getByRole("button", { name: /save workflow/i });
  await expect(saveButton).toBeDisabled();
  await expect(page.getByText(/needs an intent/i)).toBeVisible();

  await page.getByPlaceholder("intent").fill("deploy");
  await expect(saveButton).toBeEnabled();
  await saveButton.click();

  await expect(page.getByText(/workflow created/i)).toBeVisible();
  expect(saved).toHaveLength(1);
  // Creating must NOT send an id, otherwise the server would upsert over something.
  expect(saved[0].id).toBeUndefined();
  expect((saved[0].graphJson as { nodes: unknown[] }).nodes).toHaveLength(2);

  // --- Update the same definition, not a duplicate ---
  await page.getByRole("button", { name: /^edit$/i }).click();
  await page.getByRole("button", { name: "+ EndNode" }).click();
  await page.getByRole("button", { name: /save workflow/i }).click();

  await expect(page.getByText(/workflow updated/i)).toBeVisible();
  expect(saved).toHaveLength(2);
  expect(saved[1].id).toBe("wf-1");
  // The server kept one definition, so this was an update rather than a second row.
  expect((saved[1].graphJson as { nodes: unknown[] }).nodes).toHaveLength(3);
});

test("workflow view connects to the workflow server with its own credentials", async ({ page }) => {
  await stubAgent(page);

  const authCheck = async (route: import("@playwright/test").Route) => {
    const h = route.request().headers();
    expect(h["x-client-id"]).toBe("wf-client");
    expect(h["x-client-secret"]).toBe("wf-secret");
  };

  let definitions: unknown[] = [
    {
      id: "wf-1",
      name: "triage",
      version: 3,
      description: "triage an inbound request",
      createdAt: "2026-10-01T09:00:00Z",
    },
  ];
  await page.route("**/api/workflows?**", async (route) => {
    await authCheck(route);
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: definitions, nextCursor: null, hasMore: false }),
    });
  });
  // Catch-all FIRST: Playwright matches routes in reverse registration order,
  // so the specific GET route below must be registered later to win.
  await page.route("**/api/workflows/wf-1**", async (route) => {
    await authCheck(route);
    if (route.request().method() === "DELETE") {
      definitions = [];
      return route.fulfill({ status: 204, body: "" });
    }
    return route.fulfill({ status: 200, contentType: "application/json", body: "{}" });
  });
  await page.route("**/api/workflows/wf-1", async (route) => {
    await authCheck(route);
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id: "wf-1",
        name: "triage",
        version: 3,
        description: "triage an inbound request",
        graphJson: { nodes: [{ id: "start", type: "start" }] },
      }),
    });
  });

  // The workflow view is independent of any agent connection.
  await page.goto("./");
  await page.getByRole("button", { name: /accept/i }).click();
  await page.getByRole("button", { name: "Workflow", exact: true }).click();

  await expect(page.getByRole("heading", { name: "Workflows", exact: true })).toBeVisible();
  // Credentials prompt, because the workflow server is a separate service.
  await expect(page.getByLabel(/client id/i)).toBeVisible();

  await page.getByLabel(/base url/i).fill("http://127.0.0.1:8430");
  await page.getByLabel(/client id/i).fill("wf-client");
  await page.getByLabel(/client secret/i).fill("wf-secret");
  await page.getByRole("button", { name: /^connect to workflow server$/i }).click();

  // "triage" appears in both the name and the description — target the row button.
  const wfRow = page.getByRole("button", { name: /triage v3 triage an inbound/i });
  await expect(wfRow).toBeVisible();
  await expect(page.getByText("v3")).toBeVisible();

  // Expanding a definition shows its graph JSON.
  await wfRow.click();
  await expect(page.getByText(/"nodes"/)).toBeVisible();

  // The secret must not linger in component state or storage.
  const secretValue = await page.getByLabel(/client secret/i).inputValue().catch(() => null);
  expect(secretValue).toBeNull();

  const stored = await page.evaluate(() => JSON.stringify(localStorage));
  expect(stored).not.toContain("wf-secret");
});