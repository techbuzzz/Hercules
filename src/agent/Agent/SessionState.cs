using Hercules.Context;
using Hercules.LLM;
using Hercules.Memory.Layers;

namespace Hercules.Agent;

/// <summary>
///     Per-session state, externalised from <see cref="AgentCore" /> so that the agent
///     can be safely registered as a singleton and shared across parallel HTTP requests
///     (task_075 — DI lifetime fixes: captive dependency and AgentCore singleton).
///     Each <see cref="ISessionStateStore.GetOrCreate" /> call returns the same
///     <see cref="SessionState" /> instance for the same sessionId.
/// </summary>
public sealed class SessionState
{
    private readonly List<ChatTurn> _transcript = new();
    private readonly object _transcriptLock = new();
    private readonly List<ToolTraceEntry> _toolTrace = new();
    private readonly object _toolTraceLock = new();

    public SessionState(string sessionId, LayeredMemoryConfig? memoryConfig = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId must be non-empty", nameof(sessionId));
        }

        SessionId = sessionId;
        WorkingMemory = new WorkingMemoryService(memoryConfig);
        CreatedAt = DateTime.UtcNow;
    }

    public string SessionId { get; }

    public DateTime CreatedAt { get; }

    /// <summary>Last user input (used by skill-creation threshold logic).</summary>
    public string LastInput { get; set; } = "";

    /// <summary>Cached context block for legacy <c>MemoryManager.BuildContextBlock</c> path.</summary>
    public string ContextBlock { get; set; } = "";

    /// <summary>Counter incremented on every <c>HandleAsync</c> call (reflection cadence).</summary>
    public int CommandCount { get; set; }

    /// <summary>Working memory scoped to this session — replaces the singleton <c>IWorkingMemory</c> captive-dep (task_075 H6).</summary>
    public IWorkingMemory WorkingMemory { get; }

    public IReadOnlyList<ChatTurn> Transcript
    {
        get
        {
            lock (_transcriptLock)
            {
                return _transcript.ToList();
            }
        }
    }

    public void AppendTranscript(ChatTurn turn)
    {
        lock (_transcriptLock)
        {
            _transcript.Add(turn);
        }
    }

    public IReadOnlyList<ChatTurn> TakeLastTranscript(int count)
    {
        if (count <= 0)
        {
            return Array.Empty<ChatTurn>();
        }

        lock (_transcriptLock)
        {
            return _transcript.TakeLast(count).ToList();
        }
    }

    public void ClearTranscript()
    {
        lock (_transcriptLock)
        {
            _transcript.Clear();
        }
    }

    public IReadOnlyList<ToolTraceEntry> ToolTrace
    {
        get
        {
            lock (_toolTraceLock)
            {
                return _toolTrace.ToList();
            }
        }
    }

    public void AppendToolTrace(ToolTraceEntry entry)
    {
        lock (_toolTraceLock)
        {
            _toolTrace.Add(entry);
        }
    }

    public void ClearToolTrace()
    {
        lock (_toolTraceLock)
        {
            _toolTrace.Clear();
        }
    }
}
