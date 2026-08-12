using System.Text.Json;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Reflection;

/// <summary>
///     Персистентное хранилище proposals в файловой системе.
///     Файлы хранятся в: {DataRoot}/Skills/.proposals/{proposalId}.json
/// </summary>
public sealed class ProposalStore
{
    private readonly string _proposalsDir;
    private readonly JsonSerializerOptions _jsonOpts;
    private readonly ILogger<ProposalStore> _logger;
    private readonly object _lock = new();

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
    }

    /// <summary>
    ///     Сохранить proposal в файл.
    /// </summary>
    public void Save(Proposal proposal)
    {
        var filePath = GetFilePath(proposal.Id);
        var json = JsonSerializer.Serialize(proposal, _jsonOpts);
        lock (_lock)
        {
            File.WriteAllText(filePath, json);
        }

        _logger.LogDebug("Saved proposal '{Id}' to {Path}", proposal.Id, filePath);
    }

    /// <summary>
    ///     Загрузить proposal по ID.
    /// </summary>
    public Proposal? Load(string proposalId)
    {
        var filePath = GetFilePath(proposalId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        lock (_lock)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Proposal>(json, _jsonOpts);
        }
    }

    /// <summary>
    ///     Вернуть все proposals (недавние first).
    /// </summary>
    public List<Proposal> ListAll()
    {
        lock (_lock)
        {
            return Directory.GetFiles(_proposalsDir, "*.json")
                .Select(f =>
                {
                    try
                    {
                        var json = File.ReadAllText(f);
                        return JsonSerializer.Deserialize<Proposal>(json, _jsonOpts);
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(p => p is not null)
                .OrderByDescending(p => p!.CreatedAt)
                .Select(p => p!)
                .ToList();
        }
    }

    /// <summary>
    ///     Вернуть proposals для конкретного навыка.
    /// </summary>
    public List<Proposal> GetBySkill(string skillId)
    {
        return ListAll()
            .Where(p => p.SkillId == skillId)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }

    /// <summary>
    ///     Вернуть последние N proposals.
    /// </summary>
    public List<Proposal> GetRecent(int count)
    {
        return ListAll()
            .Take(count)
            .ToList();
    }

    /// <summary>
    ///     Сколько proposals создано сегодня.
    /// </summary>
    public int CountToday()
    {
        var today = DateTime.UtcNow.Date;
        return ListAll()
            .Count(p => p.CreatedAt.Date == today);
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

        lock (_lock)
        {
            File.Delete(filePath);
        }

        _logger.LogDebug("Deleted proposal '{Id}'", proposalId);
        return true;
    }

    private string GetFilePath(string proposalId) =>
        Path.Combine(_proposalsDir, $"{proposalId}.json");
}
