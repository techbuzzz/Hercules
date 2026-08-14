using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Hercules.Mesh.Abstractions;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Backends.Nats;

/// <summary>
///     Local dead-letter queue backed by a JSONL file on disk. Each line is a
///     self-contained <see cref="DlqEntry"/> that records the original task
///     payload plus the failure reason, delivery count and timestamp.
///     Thread-safe via per-append serialization and atomic file replace on
///     <see cref="RemoveAsync"/>.
///     Spec: task_074.
/// </summary>
public sealed class NatsTaskDlqStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _json;
    private readonly ILogger? _log;

    public NatsTaskDlqStore(string filePath, ILogger? log = null)
    {
        _path = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _log = log;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Absolute path to the JSONL file.</summary>
    public string FilePath => _path;

    /// <summary>Append a single DLQ entry. Safe to call concurrently.</summary>
    public async Task AppendAsync(DlqEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var line = JsonSerializer.Serialize(entry, _json);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_path, line + Environment.NewLine, Encoding.UTF8, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Append a terminated task. Convenience overload.</summary>
    public Task AppendAsync(MeshTask task, string reason, int deliveryCount, string? queueName = null, CancellationToken ct = default)
    {
        var entry = new DlqEntry
        {
            TaskId = task.Id,
            QueueName = queueName ?? task.QueueName,
            Intent = task.Intent,
            Payload = task.Payload,
            MaxRetries = task.MaxRetries,
            RetryCount = task.MaxRetries, // exhausted
            DeliveryCount = deliveryCount,
            Reason = reason,
            FailedAt = DateTimeOffset.UtcNow
        };
        return AppendAsync(entry, ct);
    }

    /// <summary>
    ///     Read up to <paramref name="limit"/> entries, optionally filtered by queue name.
    ///     Returns an empty list if the file does not exist.
    /// </summary>
    public async Task<IReadOnlyList<DlqEntry>> ListAsync(string? queueName = null, int limit = 100, CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            return Array.Empty<DlqEntry>();

        var result = new List<DlqEntry>(capacity: Math.Min(limit, 16));
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var entry = JsonSerializer.Deserialize<DlqEntry>(line, _json);
                if (entry is null)
                    continue;
                if (queueName is not null && !string.Equals(entry.QueueName, queueName, StringComparison.Ordinal))
                    continue;
                result.Add(entry);
                if (result.Count >= limit)
                    break;
            }
            catch (JsonException ex)
            {
                _log?.LogWarning(ex, "[NatsTaskDlqStore] Skipping malformed DLQ line");
            }
        }
        return result;
    }

    /// <summary>
    ///     Remove the first entry with matching <paramref name="taskId"/>. Returns true if removed.
    ///     Uses an atomic temp-file + rename for safety.
    /// </summary>
    public async Task<bool> RemoveAsync(string taskId, CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            return false;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tempPath = _path + ".tmp";
            var removed = false;
            await using (var source = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var reader = new StreamReader(source, Encoding.UTF8))
            await using (var writer = new StreamWriter(dest, Encoding.UTF8))
            {
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    if (!removed && !string.IsNullOrWhiteSpace(line))
                    {
                        DlqEntry? entry = null;
                        try { entry = JsonSerializer.Deserialize<DlqEntry>(line, _json); }
                        catch (JsonException) { /* keep malformed lines untouched */ }

                        if (entry is not null && string.Equals(entry.TaskId, taskId, StringComparison.Ordinal))
                        {
                            removed = true;
                            continue; // skip writing this line
                        }
                    }
                    await writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
                }
            }

            if (removed)
            {
                File.Move(tempPath, _path, overwrite: true);
            }
            else
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
            return removed;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Single DLQ record (one line in the JSONL file).</summary>
    public sealed class DlqEntry
    {
        public string TaskId { get; set; } = "";
        public string QueueName { get; set; } = "";
        public string Intent { get; set; } = "";
        public string Payload { get; set; } = "";
        public int MaxRetries { get; set; }
        public int RetryCount { get; set; }
        public int DeliveryCount { get; set; }
        public string Reason { get; set; } = "";
        public DateTimeOffset FailedAt { get; set; }
    }
}
