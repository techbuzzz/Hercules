using Hercules.Skills;
using Microsoft.AspNetCore.Mvc;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI контроллер шаблонов агентов (task_030).
///     Листает, показывает информацию и применяет готовые бандлы навыков+памяти+инструментов.
/// </summary>
[ApiController]
[Route("api/templates")]
public sealed class TemplateController : ControllerBase
{
    private readonly AgentTemplateManager _templates;

    public TemplateController(AgentTemplateManager templates)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    /// <summary>Список всех доступных шаблонов.</summary>
    [HttpGet]
    public ActionResult<List<TemplateEntryDto>> List()
    {
        var entries = _templates.List();
        return Ok(entries.Select(e => new TemplateEntryDto(
            e.FileName,
            e.Name,
            e.Description,
            e.Version,
            e.SkillCount,
            e.FilePath)).ToList());
    }

    /// <summary>Информация о конкретном шаблоне (манифест).</summary>
    [HttpGet("{fileName}")]
    public ActionResult<TemplateManifestDto> Get(string fileName)
    {
        try
        {
            var manifest = ReadManifest(fileName);
            return Ok(new TemplateManifestDto(
                manifest.Name,
                manifest.Description,
                manifest.Version,
                manifest.Scenario,
                manifest.Skills,
                manifest.MemoryFiles,
                manifest.ToolFiles));
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Применить шаблон: импортировать навыки, память, инструменты.</summary>
    [HttpPost("{fileName}/apply")]
    public ActionResult<ApplyTemplateResultDto> Apply(string fileName, [FromBody] ApplyTemplateRequest? body)
    {
        var resolution = body?.ConflictResolution ?? ConflictResolution.Rename;
        try
        {
            var result = _templates.Apply(fileName, resolution);
            return Ok(new ApplyTemplateResultDto(
                result.TemplateName,
                result.InstalledSkills,
                result.InstalledMemoryFiles,
                result.InstalledToolFiles,
                result.Errors,
                result.HasErrors));
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private TemplateManifest ReadManifest(string fileName)
    {
        var path = System.IO.Path.Combine(_templates.DirectoryPath, fileName);
        if (!System.IO.File.Exists(path))
        {
            throw new FileNotFoundException($"Шаблон '{fileName}' не найден.");
        }

        using var stream = System.IO.File.OpenRead(path);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var entry = archive.GetEntry("template.json")
            ?? throw new InvalidOperationException("template.json не найден в архиве шаблона.");
        using var reader = new System.IO.StreamReader(entry.Open());
        var json = reader.ReadToEnd();
        return System.Text.Json.JsonSerializer.Deserialize<TemplateManifest>(json, JsonOpts)
            ?? throw new InvalidOperationException("Не удалось десериализовать манифест шаблона.");
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
}

public sealed record TemplateEntryDto(
    string FileName,
    string Name,
    string Description,
    int Version,
    int SkillCount,
    string FilePath);

public sealed record TemplateManifestDto(
    string Name,
    string Description,
    int Version,
    string? Scenario,
    List<string> Skills,
    List<string> MemoryFiles,
    List<string> ToolFiles);

public sealed record ApplyTemplateRequest(ConflictResolution ConflictResolution = ConflictResolution.Rename);

public sealed record ApplyTemplateResultDto(
    string TemplateName,
    List<string> InstalledSkills,
    List<string> InstalledMemoryFiles,
    List<string> InstalledToolFiles,
    List<string> Errors,
    bool HasErrors);
