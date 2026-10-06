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
  await page.route("**/api/skills", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        {
          id: "s1",
          name: "answerer",
          description: "answers things",
          triggers: ["answer"],
          version: 2,
          successRate: 0.9,
          totalUses: 12,
          createdAt: "2026-01-01T00:00:00Z",
        },
      ]),
    }),
  );
  await page.route("**/api/skills/s1", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        meta: {
          id: "s1",
          name: "answerer",
          description: "answers things",
          triggers: ["answer"],
          version: 2,
          successRate: 0.9,
          totalUses: 12,
          createdAt: "2026-01-01T00:00:00Z",
        },
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

test("mesh view renders registry health and degrades gracefully", async ({ page }) => {
  await stubAgent(page);
  await page.route("**/api/mesh/agents", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
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
      ]),
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

  await connect(page);
  await page.getByRole("button", { name: "Mesh", exact: true }).click();

  await expect(page.getByRole("heading", { name: "Mesh", exact: true })).toBeVisible();
  await expect(page.getByText("Alpha")).toBeVisible();
  await expect(page.getByText("Beta")).toBeVisible();

  // Summary counters come from /api/mesh/health.
  await expect(page.getByText("closed")).toBeVisible();

  // Filtering narrows the table.
  await page.getByPlaceholder(/search/i).fill("alpha");
  await expect(page.getByText("Beta")).toHaveCount(0);
  await expect(page.getByText("Alpha")).toBeVisible();
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

test("consensus view queues human decisions and clears them after approval", async ({ page }) => {
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
  await page.getByRole("button", { name: "Consensus", exact: true }).click();

  await expect(page.getByRole("heading", { name: "Consensus", exact: true })).toBeVisible();
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