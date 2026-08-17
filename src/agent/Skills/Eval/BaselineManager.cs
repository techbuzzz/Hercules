using System.Text.Json;
using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Skills.Eval;

/// <summary>
///     Управление baseline-записями для eval harness.
///     Baselines хранятся в skills/.baselines/{skillId}.json.
/// </summary>
public sealed class BaselineManager
{
    private readonly string _baselinesDir;
    private readonly ILogger<BaselineManager> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public BaselineManager(StorageConfig storageConfig, ILogger<BaselineManager> logger)
    {
        _baselinesDir = Path.Combine(storageConfig.DataRoot, storageConfig.SkillsDir, Hercules.BuiltIn.BaselinesSubdir);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Directory.CreateDirectory(_baselinesDir);
    }

    /// <summary>
    ///     Сохранить baseline для навыка. Перезаписывает существующий baseline.
    /// </summary>
    public BaselineRecord SaveBaseline(
        string skillId,
        string skillVersion,
        SkillEvaluationResult result,
        string? recordedBy = null,
        string? reason = null)
    {
        var record = new BaselineRecord
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            SkillId = skillId,
            SkillVersion = skillVersion,
            Score = result.Score,
            TotalTests = result.TotalTests,
            PassedTests = result.PassedTests,
            RecordedAt = DateTime.UtcNow.ToString("o"),
            RecordedBy = recordedBy ?? "system",
            Reason = reason ?? "pre-promotion",
            TestResults = result.TestResults.Select(r => new TestCaseResult
            {
                Name = r.Name,
                Passed = r.Passed,
                Score = r.Passed ? 1.0 : 0.0,
                FailureReason = r.FailureReason
            }).ToList()
        };

        var path = GetPath(skillId);
        var json = JsonSerializer.Serialize(record, JsonOpts);
        File.WriteAllText(path, json);
        _logger.LogInformation("Baseline saved for skill '{SkillId}': score={Score}, id={Id}", skillId, record.Score, record.Id);
        return record;
    }

    /// <summary>
    ///     Загрузить последний baseline для навыка.
    /// </summary>
    public BaselineRecord? LoadBaseline(string skillId)
    {
        var path = GetPath(skillId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BaselineRecord>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load baseline for skill '{SkillId}'", skillId);
            return null;
        }
    }

    /// <summary>
    ///     Проверить существование baseline.
    /// </summary>
    public bool HasBaseline(string skillId) => File.Exists(GetPath(skillId));

    /// <summary>
    ///     Удалить baseline для навыка.
    /// </summary>
    public bool DeleteBaseline(string skillId)
    {
        var path = GetPath(skillId);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
            _logger.LogInformation("Baseline deleted for skill '{SkillId}'", skillId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete baseline for skill '{SkillId}'", skillId);
            return false;
        }
    }

    /// <summary>
    ///     Получить все baseline-записи (для history).
    /// </summary>
    public List<BaselineRecord> GetAllBaselines()
    {
        var records = new List<BaselineRecord>();
        if (!Directory.Exists(_baselinesDir))
        {
            return records;
        }

        foreach (var file in Directory.GetFiles(_baselinesDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize<BaselineRecord>(json, JsonOpts);
                if (record != null)
                {
                    records.Add(record);
                }
            }
            catch
            {
                // skip malformed files
            }
        }

        return records.OrderByDescending(r => r.RecordedAt).ToList();
    }

    private string GetPath(string skillId) =>
        Path.Combine(_baselinesDir, $"{skillId}.json");
}
