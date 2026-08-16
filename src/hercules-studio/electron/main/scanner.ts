import { execSync } from "node:child_process";
import type { DiscoveredAgent, ScanProgress } from "@shared/protocol";
import { getSettings } from "./settings";

type ProgressCallback = (progress: ScanProgress) => void;
let progressCallback: ProgressCallback | null = null;

export function setProgressCallback(cb: ProgressCallback | null): void {
  progressCallback = cb;
}

export function initScanner(): void {
  // No special init needed
}

export async function scanAgents(): Promise<DiscoveredAgent[]> {
  const settings = getSettings();
  const { portStart, portEnd, legacyPort, enableProcessScan, concurrent, timeoutMs } = settings.scan;

  const ports: number[] = [];
  for (let p = portStart; p <= portEnd; p++) {
    ports.push(p);
  }
  if (legacyPort !== null && !ports.includes(legacyPort)) {
    ports.push(legacyPort);
  }

  const total = ports.length;
  let scanned = 0;
  let found = 0;
  const results: DiscoveredAgent[] = [];

  // Scan in chunks of `concurrent`
  for (let i = 0; i < ports.length; i += concurrent) {
    const chunk = ports.slice(i, i + concurrent);
    const chunkResults = await Promise.all(
      chunk.map(async (port) => {
        scanned++;
        const agent = await probePort(port, timeoutMs);
        if (progressCallback) {
          progressCallback({ scanned, total, found, currentPort: port });
        }
        return agent;
      }),
    );
    for (const agent of chunkResults) {
      if (agent) {
        found++;
        results.push(agent);
        if (progressCallback) {
          progressCallback({ scanned, total, found, currentPort: agent.port });
        }
      }
    }
  }

  // Process scan (secondary discovery)
  if (enableProcessScan) {
    const processResults = scanProcesses();
    for (const agent of processResults) {
      if (!results.some((r) => r.agentId === agent.agentId && agent.agentId !== null)) {
        results.push(agent);
      }
    }
  }

  return results;
}

async function probePort(port: number, timeoutMs: number): Promise<DiscoveredAgent | null> {
  const endpoint = `http://localhost:${port}`;

  // Try to fetch manifest
  try {
    const res = await fetch(`${endpoint}/agent.manifest.json`, {
      signal: AbortSignal.timeout(timeoutMs + 500),
    });

    if (res.status === 401) {
      // Agent exists but requires auth
      return {
        port,
        agentId: null,
        displayName: null,
        endpoint,
        authRequired: true,
        foundVia: "port",
      };
    }

    if (!res.ok) {
      return null;
    }

    const manifest = (await res.json()) as { agentId?: string; displayName?: string };
    if (!manifest.agentId) {
      return null; // Not a Hercules agent
    }

    return {
      port,
      agentId: manifest.agentId,
      displayName: manifest.displayName ?? manifest.agentId,
      endpoint,
      authRequired: false,
      foundVia: "port",
    };
  } catch {
    return null;
  }
}

function scanProcesses(): DiscoveredAgent[] {
  const results: DiscoveredAgent[] = [];

  try {
    // Find Hercules.WebApi processes on Windows
    const output = execSync('tasklist /fi "imagename eq Hercules.WebApi.exe" /fo csv /nh', {
      encoding: "utf-8",
      timeout: 5000,
    });

    const lines = output.trim().split("\n").filter((l) => l.trim());
    for (const line of lines) {
      // Parse: "Hercules.WebApi.exe","1234","Console","1","50,000 K"
      const match = line.match(/"([^"]+)","(\d+)"/);
      if (!match) continue;

      const pid = Number.parseInt(match[2], 10);

      // Find listening ports for this PID
      try {
        const netOutput = execSync(`netstat -ano | findstr ":${pid}"`, {
          encoding: "utf-8",
          timeout: 5000,
        });

        const portMatches = netOutput.matchAll(/:(\d+)\s+\S+\s+\d+\s/g);
        for (const pm of portMatches) {
          const port = Number.parseInt(pm[1], 10);
          if (port >= 8421 && port <= 8521) {
            results.push({
              port,
              agentId: null, // Will be resolved by port probe
              displayName: `Hercules.WebApi (PID ${pid})`,
              endpoint: `http://localhost:${port}`,
              authRequired: false,
              foundVia: "process",
              pid,
            });
          }
        }
      } catch {
        // netstat failed for this PID, skip
      }
    }
  } catch {
    // tasklist failed (not Windows, or no processes), skip
  }

  return results;
}