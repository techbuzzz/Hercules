namespace Hercules.Supervisor;

/// <summary>Root options bound from <c>Supervisor</c> in appsettings.json.</summary>
public sealed class SupervisorOptions
{
    public const string SectionName = "Supervisor";

    /// <summary>Base URL of the agent's REST API.</summary>
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:8421";

    /// <summary>
    /// System-role API key. Exchanged once for a short-lived session token at
    /// startup, mirroring the browser client (ADR-0009) so the long-lived secret
    /// is not attached to every poll.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>How often to ask the agent whether a restart is pending.</summary>
    public int RestartPollSeconds { get; set; } = 5;

    /// <summary>Grace period between close-request and kill during a restart.</summary>
    public int RestartGraceSeconds { get; set; } = 10;

    /// <summary>
    /// Consecutive failed health checks before an agent is restarted.
    /// Prevents restart loops on a transient blip.
    /// </summary>
    public int UnhealthyThreshold { get; set; } = 3;

    /// <summary>Minimum seconds between two restarts of the same agent.</summary>
    public int RestartCooldownSeconds { get; set; } = 30;

    /// <summary>Port the supervisor's own status API listens on.</summary>
    public int StatusPort { get; set; } = 8479;

    /// <summary>Directory for per-agent stdout/stderr capture.</summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>Start the configured agents on supervisor boot.</summary>
    public bool AutoStart { get; set; } = true;

    public List<AgentOptions> Agents { get; set; } = [];
}

/// <summary>One supervised agent process.</summary>
public sealed class AgentOptions
{
    public string Name { get; set; } = "";

    /// <summary>Executable to launch.</summary>
    public string Command { get; set; } = "";

    public List<string> Arguments { get; set; } = [];

    public string? WorkingDirectory { get; set; }

    /// <summary>Environment variables passed to the child, on top of the inherited set.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>URL polled to decide whether the agent is alive.</summary>
    public string HealthUrl { get; set; } = "";

    public int HealthIntervalSeconds { get; set; } = 15;

    /// <summary>Restart automatically when health checks keep failing.</summary>
    public bool AutoRestart { get; set; } = true;

    /// <summary>
    /// The agent that an operator-requested restart (<c>POST /api/system/restart</c>)
    /// applies to. Exactly one agent should set this.
    /// </summary>
    public bool Primary { get; set; }
}