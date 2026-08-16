using System.Collections.Immutable;
using System.Text.Json;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Reflection;

/// <summary>
///     Персистентное хранилище proposals в файловой системе.
///     Файлы хранятся в: {DataRoot}/Skills/.proposals/{proposalId}.json
///     <para>
///         task_084: in-memory snapshot of all proposals invalidated by
///         <see cref="FileSystemWatcher" /> events plus a periodic safety refresh.
///         Read paths (<see cref="ListAll" />, <see cref="GetBySkill" />,
///         <see cref="GetRecent" />, <see cref="CountToday" />) are lock-free.
///     </para>
/// </summary>
public sealed class ProposalStore : IDisposable
{
    private readonly string _proposalsDir;
    private readonly JsonSerializerOptions _jsonOpts;
    private readonly ILogger<ProposalStore> _logger;
    private readonly object _writeLock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _periodicRefresh;
    private readonly TimeSpan _periodicRefreshInterval = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _watcherDebounce = TimeSpan.FromMilliseconds(250);

    private ImmutableList<Proposal> _snapshot = ImmutableList<Proposal>.Empty;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _watcherDebounceCts;
    private bool _disposed;

    public ProposalStore(StorageConfig storageConfig, ILogger<ProposalStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _proposalsDir = Path.Combine(
            storageConfig.DataRoot,
            storageConfig.SkillsDir,
            ".proposals");

        if (!Directory.Exists(_proposalsDir))
        {
            Directory.CreateDirectory(_proposalsDir);
            _logger.LogInformation("Created proposals directory: {Dir}", _proposalsDir);
        }

        // Eagerly populate the snapshot from disk (synchronous on ctor; small N).
        _snapshot = ReadAllFromDiskSync().ToImmutableList();

        try
        {
            _watcher = new FileSystemWatcher(_proposalsDir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnWatcherChanged;
            _watcher.Changed += OnWatcherChanged;
            _watcher.Deleted += OnWatcherChanged;
            _watcher.Renamed += OnWatcherRenamed;
            _watcher.Error += OnWatcherError;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start FileSystemWatcher on {Dir} — relying on periodic refresh", _proposalsDir);
        }

        _periodicRefresh = new Timer(
            _ => _ = SafeRefreshAsync(isFullScan: true),
            state: null,
            dueTime: _periodicRefreshInterval,
            period: _periodicRefreshInterval);
    }

    /// <summary>
    ///     Сохранить proposal в файл.
    /// </summary>
    public void Save(Proposal proposal)
    {
        var filePath = GetFilePath(proposal.Id);
        var json = JsonSerializer.Serialize(proposal, _jsonOpts);

        lock (_writeLock)
        {
            File.WriteAllText(filePath, json);
        }

        // FileSystemWatcher will normally fire; update the snapshot eagerly as well
        // so subsequent reads don't wait for the debounce window.
        UpsertSnapshot(proposal);
        _logger.LogDebug("Saved proposal '{Id}' to {Path}", proposal.Id, filePath);
    }

    /// <summary>
    ///     Загрузить proposal по ID.
    /// </summary>
    public Proposal? Load(string proposalId)
    {
        // Try the snapshot first (no I/O).
        var existing = _snapshot.FirstOrDefault(p => p.Id == proposalId);
        if (existing is not null)
        {
            return existing;
        }

        var filePath = GetFilePath(proposalId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        lock (_writeLock)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Proposal>(json, _jsonOpts);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read proposal '{Id}' from {Path}", proposalId, filePath);
                return null;
            }
        }
    }

    /// <summary>
    ///     Вернуть все proposals (недавние first). Lock-free snapshot read.
    /// </summary>
    public List<Proposal> ListAll()
    {
        return _snapshot
            .OrderByDescending(p => p.CreatedAt)
            .Select(Clone)
            .ToList();
    }

    /// <summary>
    ///     Asynchronously read every proposal file from disk and return a fresh
    ///     ordered list. Bypasses the cache — useful for tests and recovery paths.
    /// </summary>
    public async Task<List<Proposal>> ListAllAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_proposalsDir, "*.json");
        var results = new List<Proposal>(files.Length);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var proposal = JsonSerializer.Deserialize<Proposal>(json, _jsonOpts);
                if (proposal is not null)
                {
                    results.Add(proposal);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read proposal file {File}", file);
            }
        }

        var ordered = results.OrderByDescending(p => p.CreatedAt).ToList();
        _snapshot = ordered.ToImmutableList();
        return ordered.Select(Clone).ToList();
    }

    /// <summary>
    ///     Вернуть proposals для конкретного навыка.
    /// </summary>
    public List<Proposal> GetBySkill(string skillId)
    {
        return _snapshot
            .Where(p => p.SkillId == skillId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(Clone)
            .ToList();
    }

    /// <summary>
    ///     Вернуть последние N proposals.
    /// </summary>
    public List<Proposal> GetRecent(int count)
    {
        return _snapshot
            .OrderByDescending(p => p.CreatedAt)
            .Take(count)
            .Select(Clone)
            .ToList();
    }

    /// <summary>
    ///     Сколько proposals создано сегодня.
    /// </summary>
    public int CountToday()
    {
        var today = DateTime.UtcNow.Date;
        return _snapshot.Count(p => p.CreatedAt.Date == today);
    }

    /// <summary>
    ///     Удалить proposal (например, после superseded).
    /// </summary>
    public bool Delete(string proposalId)
    {
        var filePath = GetFilePath(proposalId);
        if (!File.Exists(filePath))
        {
            return false;
        }

        lock (_writeLock)
        {
            File.Delete(filePath);
        }

        // Drop from snapshot eagerly.
        _snapshot = _snapshot.RemoveAll(p => p.Id == proposalId);
        _logger.LogDebug("Deleted proposal '{Id}'", proposalId);
        return true;
    }

    /// <summary>
    ///     Force a full rescan from disk and update the snapshot. Safe to call
    ///     concurrently — coalesces via an internal semaphore.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await SafeRefreshAsync(isFullScan: true, ct).ConfigureAwait(false);
    }

    private async Task SafeRefreshAsync(bool isFullScan, CancellationToken ct = default)
    {
        try
        {
            if (!await _refreshLock.WaitAsync(0, ct).ConfigureAwait(false))
            {
                // Another refresh in flight — skip to coalesce.
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (isFullScan)
            {
                _snapshot = ReadAllFromDiskSync().ToImmutableList();
            }
            else
            {
                // Incremental: re-read single file by name.
                // (No-op here; Save() already updates the snapshot.)
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProposalStore refresh failed");
        }
        finally
        {
            try
            {
                _refreshLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // store already disposed
            }
        }
    }

    private List<Proposal> ReadAllFromDiskSync()
    {
        var results = new List<Proposal>();
        if (!Directory.Exists(_proposalsDir))
        {
            return results;
        }

        foreach (var file in Directory.GetFiles(_proposalsDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var proposal = JsonSerializer.Deserialize<Proposal>(json, _jsonOpts);
                if (proposal is not null)
                {
                    results.Add(proposal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read proposal file {File}", file);
            }
        }

        return results.OrderByDescending(p => p.CreatedAt).ToList();
    }

    private void UpsertSnapshot(Proposal proposal)
    {
        ImmutableInterlocked.Update(ref _snapshot, snapshot =>
        {
            var idx = snapshot.FindIndex(p => p.Id == proposal.Id);
            if (idx < 0)
            {
                return snapshot.Add(proposal);
            }

            return snapshot.SetItem(idx, proposal);
        });
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e) =>
        ScheduleDebouncedRefresh();

    private void OnWatcherRenamed(object sender, RenamedEventArgs e) =>
        ScheduleDebouncedRefresh();

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "FileSystemWatcher error on {Dir} — falling back to periodic refresh", _proposalsDir);
    }

    private void ScheduleDebouncedRefresh()
    {
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _watcherDebounceCts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_watcherDebounce, newCts.Token).ConfigureAwait(false);
                await SafeRefreshAsync(isFullScan: true).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer event
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Debounced refresh failed");
            }
        });
    }

    /// <summary>
    ///     Defensive copy so callers cannot mutate the snapshot entry's
    ///     mutable collections (List&lt;string&gt; for ProposedPhrases/RemovedPhrases).
    /// </summary>
    private static Proposal Clone(Proposal source) => new()
    {
        Id = source.Id,
        SkillId = source.SkillId,
        SkillName = source.SkillName,
        CurrentVersion = source.CurrentVersion,
        RollbackVersion = source.RollbackVersion,
        AnalysisSummary = source.AnalysisSummary,
        ProposedPrompt = source.ProposedPrompt,
        ProposedPhrases = new List<string>(source.ProposedPhrases),
        RemovedPhrases = new List<string>(source.RemovedPhrases),
        ExpectedScoreGain = source.ExpectedScoreGain,
        CurrentScore = source.CurrentScore,
        PredictedScore = source.PredictedScore,
        TriggeredBy = source.TriggeredBy,
        CreatedAt = source.CreatedAt,
        ResolvedAt = source.ResolvedAt,
        ResolvedBy = source.ResolvedBy,
        Status = source.Status,
        EvalResult = source.EvalResult,
        RejectionReason = source.RejectionReason
    };

    private string GetFilePath(string proposalId) =>
        Path.Combine(_proposalsDir, $"{proposalId}.json");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _watcher?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing FileSystemWatcher");
        }

        _periodicRefresh.Dispose();
        _refreshLock.Dispose();

        var cts = Interlocked.Exchange(ref _watcherDebounceCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
