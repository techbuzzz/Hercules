using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Audit;

/// <summary>
///     JSON Lines file sink for inter-agent audit (task_041).
///     One JSON object per line, auto-rotated by date.
///     File: {directory}/mesh-audit-{yyyy-MM-dd}.jsonl
/// </summary>
public sealed class FileMeshAuditSink : IAuditSink
{
    private readonly string _directory;
    private readonly ILogger<FileMeshAuditSink> _logger;
    private readonly SemaphoreSlim _sem = new(1, 1);
    private readonly JsonSerializerOptions _jsonOpts;
    private bool _disposed;

    public string Name => "FileMeshAuditSink";
    public bool IsEnabled => !_disposed;

    public FileMeshAuditSink(string directory, ILogger<FileMeshAuditSink> logger)
    {
        _directory = !string.IsNullOrWhiteSpace(directory) ? directory : "mesh-audit";
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MeshAudit.FileSink] Failed to create directory {Dir}", _directory);
        }
    }

    public async Task WriteAsync(InterAgentAuditRecord record, CancellationToken ct = default)
    {
        if (_disposed) return;

        var filePath = GetFilePath();

        try
        {
            var line = JsonSerializer.Serialize(record, _jsonOpts);
            await _sem.WaitAsync(ct);
            try
            {
                await File.AppendAllTextAsync(filePath, line + Environment.NewLine, ct);
            }
            finally
            {
                _sem.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MeshAudit.FileSink] Failed to write audit record to {File}", filePath);
        }
    }

    private string GetFilePath()
    {
        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(_directory, $"mesh-audit-{dateStr}.jsonl");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sem.Dispose();
    }
}
