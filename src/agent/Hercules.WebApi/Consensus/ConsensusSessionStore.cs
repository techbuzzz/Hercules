using System.Text.Json;
using System.Text.Json.Serialization;
using Hercules.Config;

namespace Hercules.WebApi.Consensus;

/// <summary>One agent's answer inside a stored consensus session.</summary>
public sealed record ConsensusAnswer(string AgentName, string ConnectionId, string Answer);

/// <summary>
/// A completed (or attempted) consensus round, kept so the operator can look back at it.
/// <para>
/// Persisted rather than held in memory: a consensus round is a record of what several
/// agents said, and an in-memory store would lose it on restart — which is exactly when
/// someone wants to review why an answer was picked. Stored as JSON under
/// <c>{DataRoot}/consensus</c>, matching how other Studio-owned history is kept.
/// </para>
/// </summary>
public sealed record ConsensusSession(
    string Id,
    string Prompt,
    IReadOnlyList<string> SelectedAgents,
    IReadOnlyList<ConsensusAnswer> Responses,
    string AggregationMode,
    string? Result,
    string? JudgeRationale,
    DateTimeOffset CreatedAt);

/// <summary>
/// File-backed store for consensus history (Stage 7.8).
/// <para>
/// Bounded by <see cref="MaxSessions"/> and pruned oldest-first. Agent answers are kept
/// verbatim: the point of the history is to review what was actually said, so there is no
/// summarising here — the disk cost is bounded by the cap instead.
/// </para>
/// </summary>
public sealed class ConsensusSessionStore
{
    /// <summary>Retained sessions; oldest are dropped beyond this.</summary>
    public const int MaxSessions = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly ILogger<ConsensusSessionStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ConsensusSession> _sessions = [];

    public ConsensusSessionStore(StorageConfig storage, ILogger<ConsensusSessionStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(storage.DataRoot, BuiltIn.ConsensusSubdir);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "sessions.json");
        Load();
    }

    public ConsensusSession Save(ConsensusSession session)
    {
        _gate.Wait();
        try
        {
            _sessions.RemoveAll(s => s.Id == session.Id);
            _sessions.Add(session);
            _sessions.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            if (_sessions.Count > MaxSessions)
            {
                _sessions.RemoveRange(MaxSessions, _sessions.Count - MaxSessions);
            }

            Persist();
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ConsensusSession> List(int limit = 50)
    {
        _gate.Wait();
        try
        {
            return _sessions.Take(Math.Clamp(limit, 1, MaxSessions)).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public ConsensusSession? Get(string id)
    {
        _gate.Wait();
        try
        {
            return _sessions.FirstOrDefault(s => s.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<List<ConsensusSession>>(File.ReadAllText(_path), Json);
            if (loaded is null) return;
            _sessions.AddRange(loaded);
            _sessions.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        }
        catch (Exception ex)
        {
            // History is not worth failing startup over; a corrupt file starts empty.
            _logger.LogWarning("[Consensus] Could not read consensus history: {Message}", ex.Message);
            _sessions.Clear();
        }
    }

    private void Persist()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_sessions, Json));
            // Write-then-move so a crash mid-write cannot truncate the history.
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Consensus] Could not persist consensus history: {Message}", ex.Message);
        }
    }
}