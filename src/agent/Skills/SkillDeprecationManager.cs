using System.Text.Encodings.Web;
using System.Text.Json;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Skills;

/// <summary>
///     Управление депрекацией и откатом навыков.
///     Deprecate: помечает навык deprecated в meta.json.
///     Rollback: восстанавливает предыдущую версию (.v{N-1}.md).
///     Навыки с DeprecatedAt != null не маршрутизируются роутером (agent-side фильтрация).
/// </summary>
public sealed class SkillDeprecationManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly FileSkillRepository _repo;
    private readonly ILogger<SkillDeprecationManager> _logger;

    public SkillDeprecationManager(FileSkillRepository repo, ILogger<SkillDeprecationManager> logger)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Пометить навык как deprecated. Записывает DeprecatedAt и DeprecationReason в meta.json.
    /// </summary>
    /// <param name="id">ID навыка.</param>
    /// <param name="reason">Причина депрекации.</param>
    /// <returns>Обновлённый навык или null, если не найден.</returns>
    public Skill? Deprecate(string id, string reason)
    {
        Skill? skill = _repo.Load(id);
        if (skill is null)
        {
            _logger.LogWarning("Deprecate: навык '{Id}' не найден", id);
            return null;
        }

        skill.Meta.DeprecatedAt = DateTime.UtcNow.ToString("o");
        skill.Meta.DeprecationReason = reason;
        _repo.Save(skill);
        _logger.LogInformation("Навык '{Name}' ({Id}) помечен deprecated: {Reason}",
            skill.Meta.Name, id, reason);
        return skill;
    }

    /// <summary>
    ///     Откатить навык к предыдущей версии (.v{N-1}.md).
    ///     Если текущая версия = 1 — откат невозможен.
    /// </summary>
    /// <param name="id">ID навыка.</param>
    /// <returns>Обновлённый навык или null (не найден / нет предыдущей версии).</returns>
    public Skill? Rollback(string id)
    {
        Skill? skill = _repo.Load(id);
        if (skill is null)
        {
            _logger.LogWarning("Rollback: навык '{Id}' не найден", id);
            return null;
        }

        if (skill.Meta.Version <= 1)
        {
            _logger.LogWarning("Rollback: навык '{Name}' уже на версии 1, откат невозможен", skill.Meta.Name);
            return null;
        }

        var prevVersionPath = Path.Combine(_repo.SkillsDirectory, $"skill.{id}.v{skill.Meta.Version - 1}.md");
        if (!File.Exists(prevVersionPath))
        {
            _logger.LogWarning("Rollback: файл предыдущей версии не найден: {Path}", prevVersionPath);
            return null;
        }

        var prevDescription = File.ReadAllText(prevVersionPath);
        skill.Meta.Version -= 1;
        skill.Description = prevDescription;
        _repo.Save(skill);
        _logger.LogInformation("Навык '{Name}' ({Id}) откащен к версии {Version}",
            skill.Meta.Name, id, skill.Meta.Version);
        return skill;
    }

    /// <summary>
    ///     Список всех deprecated-навыков.
    /// </summary>
    public List<Skill> GetDeprecated()
    {
        return _repo.LoadAll()
            .Where(s => !string.IsNullOrEmpty(s.Meta.DeprecatedAt))
            .ToList();
    }

    /// <summary>
    ///     Снять deprecated-статус с навыка (например, после исправления).
    /// </summary>
    public Skill? Undeprecate(string id)
    {
        Skill? skill = _repo.Load(id);
        if (skill is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(skill.Meta.DeprecatedAt))
        {
            return skill; // Уже активен
        }

        skill.Meta.DeprecatedAt = null;
        skill.Meta.DeprecationReason = null;
        _repo.Save(skill);
        _logger.LogInformation("Навык '{Name}' ({Id}) возвращён в активные", skill.Meta.Name, id);
        return skill;
    }
}
