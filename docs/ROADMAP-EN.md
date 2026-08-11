# Roadmap

This document describes the planned evolution of **Hercules** — a tiny, self-improving AI micro-agent on C# / .NET 10. The goal is not to build one large assistant, but to make the smallest possible autonomous unit that can later be composed into an **agent mesh**: a network of specialized micro-agents that discover, call, and learn from each other, similar to how microservices form an application architecture.

> For the current state of the project, see [CHANGELOG-EN.md](../CHANGELOG-EN.md).  
> For the mesh architecture concept, see [docs/AGENT-MESH-EN.md](AGENT-MESH-EN.md).

---

## Design constraints for a micro-agent

Every feature on this roadmap is evaluated against these constraints:

- **Single responsibility** — one agent does one thing well.
- **Small memory and disk footprint** — runs on modest hardware.
- **Self-contained runtime** — one executable, one config, one data folder.
- **OpenAI-compatible interface** — any LLM provider, local or cloud; edge devices use external LLM.
- **Discoverable and callable** — via lightweight protocol (HTTP/gRPC or message bus).
- **Versioned skills** — reusable, shareable, rollback-capable.
- **Edge-ready** — runs on Raspberry Pi with external LLM, buffers data offline.

---

## Phase 1 — Single autonomous unit (now → Q3 2026)

Goal: prove that one Hercules agent can operate independently, learn from its own traffic, and expose a clean API.

```mermaid
flowchart LR
    User["User / Telegram / Web UI"] -->|request| AgentCore["AgentCore"]
    AgentCore -->|route| Skills["Skills (local)"]
    AgentCore -->|fallback| LLM["LLM provider (external)"]
    AgentCore -->|read/write| Memory["Memory (Markdown/JSON)"]
    AgentCore -->|log| SQLite["SQLite metrics"]
    AgentCore -->|response| User
```

| # | Initiative | Outcome |
| - | ---------- | ------- |
| 1 | **Core agent loop** | `AgentCore` handles a request, routes it to a skill or direct LLM call, updates memory, and logs metrics. |
| 2 | **Skill lifecycle** | Skill creation, versioning, improvement, and deletion are fully automated with human-in-the-loop for high-risk changes. |
| 3 | **Hybrid storage** | Markdown/JSON for skills and memory + SQLite for logs and metrics; no external dependencies. |
| 4 | **Multi-provider LLM** | YandexGPT, Ollama Cloud/Local, LM Studio through `Microsoft.Extensions.AI` with automatic fallback. |
| 5 | **Interfaces** | CLI REPL, Telegram bot, ASP.NET Core Minimal API, Astro web UI. |
| 6 | **Tests and benchmarks** | `dotnet test` ≥ 70 % coverage; `dotnet run --benchmark` measures skill hit rate, latency, and memory growth. |

**Deliverable:** a standalone micro-agent anyone can run locally.

---

## Phase 2 — Composable skills and tool use (Q4 2026)

Goal: turn skills into portable, self-contained units that can be imported, exported, and chained. This is the foundation for **agent templates** used in IoT deployments.

| # | Initiative | Outcome |
| - | ---------- | ------- |
| 7 | **Skill package format** | A skill is a folder with `skill.meta.json`, `skill.prompt.md`, `skill.tests.json`, and optional `tool.schema.json`. |
| 8 | **Skill marketplace** | `data/Skills/marketplace/` with import/export CLI commands and a public template repository. |
| 9 | **Semantic routing** | `SkillRouter` ranks skills by query embedding similarity, not just keyword triggers. |
| 10 | **Tool registry** | Skills declare tools (HTTP, file-system, shell, database, GPIO/MQTT) loaded from `data/Tools/` with allow/deny lists. |
| 11 | **Agent templates** | Pre-built skill + memory + tool bundles for vertical scenarios (greenhouse, energy, cold-chain, server closet). |

**Deliverable:** one agent can compose multiple skills and tools internally, and a new vertical deployment starts from a template, not from scratch.

---

## Phase 3 — Inter-agent protocol (Q1 2027)

Goal: make agents talk to each other over a lightweight, language-agnostic protocol.

```mermaid
flowchart LR
    AgentA["Agent A\nmanifest + skills"] -->|intent envelope| Bus["HTTP / gRPC / message bus"]
    Bus -->|intent envelope| AgentB["Agent B\nmanifest + skills"]
    Registry[("Capability registry")] -->|lookup| Bus
```

| # | Initiative | Outcome |
| - | ---------- | ------- |
| 12 | **Agent manifest** | Each agent publishes `agent.manifest.json`: name, version, capabilities, skills, endpoint, auth method. |
| 13 | **Capability registry** | A local registry (file or SQLite) lists known agents and what each can do. |
| 14 | **Inter-agent message format** | Standard JSON envelope: `requestId`, `sender`, `intent`, `payload`, `replyTo`, `timeout`. |
| 15 | **Transport options** | HTTP/gRPC endpoints plus optional message-bus adapter (RabbitMQ, NATS, Azure Service Bus). |
| 16 | **Discovery mechanisms** | Static config, mDNS/Bonjour, and registry lookup. |

**Deliverable:** two Hercules agents can discover each other and route a request from one to the other.

---

## Phase 4 — Mesh orchestration (Q2 2027)

Goal: a network of micro-agents behaves like one coherent agent system, with capability-aware routing, bounded delegation, retries, observability, shared learning, and optional distributed backends for larger meshes.

```mermaid
flowchart TD
    User["User request"] --> Router["Mesh router"]
    Router -->|2a. local skill| Local["Local skills"]
    Router -->|2b. forward intent| Peer["Best peer agent"]
    Router -->|2c. fan-out| Peers["Peer A\nPeer B\nPeer C"]
    Peers --> Judge["Verifier / LLM judge"]
    Local --> Response["Typed response"]
    Peer --> Response
    Judge --> Response
    Response --> User
    Eval["Distributed reflection"] --> Skills["Candidate skill versions"]
```

| # | Initiative | Outcome |
| - | ---------- | ------- |
| 17 | **Mesh router** | When a local skill is missing, ineligible, or below its confidence threshold, the agent forwards the request to the most suitable trusted peer agent based on capability, policy, health, latency, and expected quality. |
| 18 | **Fan-out / fan-in** | A request can be broadcast to several eligible agents under strict concurrency and budget limits; responses are validated against schemas and selected by deterministic rules, voting, or an optional judge model. |
| 19 | **Retry and circuit breaker** | Failed peer calls use deadline-aware retries, exponential backoff with jitter, per-peer circuit breakers, and bulkheads. Non-idempotent operations are never retried blindly and require idempotency keys. |
| 20 | **Distributed reflection** | Reflection reports include peer-agent performance, routing decisions, failure patterns, and suggest new skills, routing rules, or peer relationships. They create proposals, not unreviewed production changes. |
| 21 | **Shared memory sync** | Selected memory facts and skills can be synchronised between trusted agents using explicit namespaces, provenance, conflict-resolution rules, TTL, encryption in transit, and per-field data-classification policy. |
| 22 | **Verification pipeline** | High-impact or safety-sensitive answers can be checked by verifier skills, numeric validators, policy enforcers, or independent peer agents before being returned or acted upon. |
| 23 | **Delegation boundaries** | The mesh limits hop count, fan-out width, cumulative tool calls, total cost, and elapsed time per request. Agents may decline delegation rather than exceeding their policy or capacity. |
| 24 | **Human-in-the-loop escalation** | Ambiguous, low-confidence, destructive, or policy-sensitive operations are escalated with a concise action plan and context for human approval. Execution gates are enforced by code, not by prompts alone. |
| 25 | **Mesh observability** | Every local and inter-agent step emits correlated traces, metrics, and structured logs. Mesh traffic, routing decisions, retries, and backing-store interactions are visible and attributable per request and per agent. |
| 26 | **Mesh backends abstraction** | `IMeshBus`, `ITaskQueue`, and `IMeshStateStore` interfaces decouple mesh orchestration from concrete backends. A single-host mesh continues to run with in-process queues and SQLite by default. |
| 27 | **Redis/Valkey coordination backend** | Optional RESP-compatible in-memory backend (Redis or Valkey) provides working memory, distributed locks, and ephemeral queues for coordination at higher concurrency. Durable truth remains in SQLite/PostgreSQL. |
| 28 | **NATS / JetStream transport option** | Optional NATS messaging backbone provides subject-based routing, queue groups for load-balanced agent workers, and JetStream streams for durable, at-least-once delivery and replay when agents disconnect. |
| 29 | **PostgreSQL shared state backend** | Optional PostgreSQL-backed state store holds cross-agent workflow state, shared skill registries, evaluation records, and audit logs. Job queues use `SKIP LOCKED` patterns for moderate-throughput agent execution. |
| 30 | **Backend profiles and degradation** | Deployment profiles declare whether the mesh uses only local SQLite, Redis/Valkey, NATS, PostgreSQL, or combinations. If a backend becomes unavailable, agents degrade to local-only mode or stop accepting new delegations according to policy, instead of failing silently. |
| 31 | **Mesh evaluation suite** | Reproducible scenarios measure task success, safety denials, routing quality, latency, cost, resilience, and degradation when a peer, tool, backend, or LLM provider fails or becomes slow. |

**Deliverable:** a mesh of 3–5 Hercules agents can safely answer queries that no single agent could answer alone, with bounded cost and explainable delegation. Larger swarms can opt into Redis/Valkey, NATS, and PostgreSQL backends through configuration profiles, without making any external service mandatory for a single local agent.

---

## Phase 5 — IoT/edge fleet and mesh operations (Q3 2027)

Goal: make the mesh production-ready, observable, and governable — including fleets of low-cost edge devices.

| # | Initiative | Outcome |
| - | ---------- | ------- |
| 32 | **Mesh dashboard** | Web UI shows live agent topology, traffic between agents, per-agent health, and skill usage heatmap. |
| 33 | **Centralized logging and tracing** | Every inter-agent call has a `traceId`; logs can be shipped to OpenTelemetry/Loki/etc. |
| 34 | **Identity and trust** | Mutual TLS or API-key trust between agents; per-agent access control lists for skills and memory. |
| 35 | **Rate limiting and quotas** | Per-agent and per-skill rate limits; LLM cost budgets across the mesh. |
| 36 | **Lifecycle management** | CLI and API to start, stop, update, and rollback agents in the mesh. |
| 37 | **Edge provisioning** | SD-card image / Docker image for Raspberry Pi with first-boot Wi-Fi and API-key activation flow. |
| 38 | **Offline resilience** | Agent buffers sensor logs and outgoing alerts; syncs with mesh/cloud when connectivity returns. |
| 39 | **Fleet templates** | One template per vertical (greenhouse, cold-chain, closet, vending) with validated hardware bill of materials. |

**Deliverable:** Hercules mesh can be deployed as a set of small services behind a gateway, and as a fleet of Raspberry Pi edge agents, with operational visibility.

---

## Long-term vision

Hercules becomes a **runtime for agent meshes**: tiny, self-improving, single-purpose agents that discover each other, delegate work, share skills, and learn collectively. A mesh can live on one machine, across a LAN, or in the cloud — composed like microservices, but with built-in reasoning, memory, and adaptation.

---

## How to influence the roadmap

- Open a [discussion](../../discussions) for ideas.
- Open an [issue](../../issues) for concrete bugs or proposals.
- See [CONTRIBUTING-EN.md](../CONTRIBUTING-EN.md) for contribution guidelines.
