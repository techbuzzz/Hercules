using Hercules.Skills;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI маршруты шаблонов агентов (task_030, task_112).
///     Листает, показывает информацию и применяет готовые бандлы навыков+памяти+инструментов.
///     Делегирует в <see cref="AgentTemplateManager"/>.
/// </summary>
public static class TemplateController
{
    public static void MapTemplate(this IEndpointRouteBuilder app)
    {
        // GET /api/templates — список всех доступных шаблонов
        app.MapGet("/api/templates", (AgentTemplateManager templates) =>
        {
            var entries = templates.List();
            return Results.Ok(entries.Select(e => new TemplateEntryDto(
                e.FileName,
                e.Name,
                e.Description,
                e.Version,
                e.SkillCount,
                e.FilePath)).ToList());
        }).WithName("ListTemplates");

        // GET /api/templates/{fileName} — информация о конкретном шаблоне (манифест)
        app.MapGet("/api/templates/{fileName}", (AgentTemplateManager templates, string fileName) =>
        {
            try
            {
                var manifest = ReadManifest(templates, fileName);
                return Results.Ok(new TemplateManifestDto(
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
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("GetTemplate");

        // POST /api/templates/{fileName}/apply — применить шаблон (импортировать навыки, память, инструменты)
        app.MapPost("/api/templates/{fileName}/apply", (
            AgentTemplateManager templates,
            string fileName,
            ApplyTemplateRequest? body) =>
        {
            var resolution = body?.ConflictResolution ?? ConflictResolution.Rename;
            try
            {
                var result = templates.Apply(fileName, resolution);
                return Results.Ok(new ApplyTemplateResultDto(
                    result.TemplateName,
                    result.InstalledSkills,
                    result.InstalledMemoryFiles,
                    result.InstalledToolFiles,
                    result.Errors,
                    result.HasErrors));
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("ApplyTemplate");
    }

    private static TemplateManifest ReadManifest(AgentTemplateManager templates, string fileName)
    {
        var path = Path.Combine(templates.DirectoryPath, fileName);
        if (!System.IO.File.Exists(path))
        {
            throw new FileNotFoundException($"Шаблон '{fileName}' не найден.");
        }

        using var stream = System.IO.File.OpenRead(path);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var entry = archive.GetEntry("template.json")
            ?? throw new InvalidOperationException("template.json не найден в архиве шаблона.");
        using var reader = new StreamReader(entry.Open());
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
