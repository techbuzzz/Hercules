using Hercules.Skills;
using Hercules.Skills.Marketplace;
using Hercules.Storage;
using Microsoft.AspNetCore.Mvc;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI контроллер маркетплейса навыков (task_021).
///     CRUD для пакетов, верификация, импорт по URL.
/// </summary>
[ApiController]
[Route("api/marketplace")]
public sealed class MarketplaceController : ControllerBase
{
    private readonly SkillMarketplace _marketplace;
    private readonly SkillPackager _packager;

    public MarketplaceController(SkillMarketplace marketplace, SkillPackager packager)
    {
        _marketplace = marketplace;
        _packager = packager;
    }

    /// <summary>Список всех пакетов в маркетплейсе.</summary>
    [HttpGet]
    public ActionResult<List<MarketplaceEntry>> List()
    {
        return Ok(_marketplace.List());
    }

    /// <summary>Поиск пакетов по запросу.</summary>
    [HttpGet("search")]
    public ActionResult<List<MarketplaceEntry>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(_marketplace.List());
        }
        return Ok(_marketplace.Search(q));
    }

    /// <summary>Проверить integrity (hash + signature) пакета.</summary>
    [HttpGet("{file}/verify")]
    public ActionResult<PackageVerificationResult> Verify(string file)
    {
        var result = _marketplace.VerifyPackage(file);
        if (!result.IsValid && result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    /// <summary>Получить зависимости пакета.</summary>
    [HttpGet("{file}/deps")]
    public ActionResult<List<DependencyInfo>> GetDeps(string file)
    {
        var deps = _marketplace.GetDependencies(file);
        return Ok(deps);
    }

    /// <summary>Установить пакет из маркетплейса.</summary>
    [HttpPost("install")]
    public ActionResult<Skill> Install([FromBody] InstallRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FileName))
        {
            return BadRequest("fileName is required.");
        }

        try
        {
            var skill = _marketplace.Install(req.FileName, req.ConflictResolution);
            return Ok(skill.Meta);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>Установить пакет со всеми зависимостями.</summary>
    [HttpPost("install-with-deps")]
    public ActionResult InstallWithDeps([FromBody] InstallRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FileName))
        {
            return BadRequest("fileName is required.");
        }

        try
        {
            var skills = _marketplace.InstallWithDeps(req.FileName);
            return Ok(skills.Select(s => s.Meta).ToList());
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>Удалить пакет из маркетплейса.</summary>
    [HttpDelete("{file}")]
    public ActionResult Remove(string file)
    {
        var removed = _marketplace.Remove(file);
        if (!removed)
        {
            return NotFound($"Package '{file}' not found in marketplace.");
        }
        return NoContent();
    }

    /// <summary>Опубликовать .skillpkg в маркетплейс (multipart upload).</summary>
    [HttpPost("publish")]
    [RequestSizeLimit(52428800)] // 50 MB
    public async Task<ActionResult> Publish(IFormFile package, CancellationToken ct)
    {
        if (package is null || package.Length == 0)
        {
            return BadRequest("package is required.");
        }

        try
        {
            // Save to temp file, then publish
            var tempPath = Path.Combine(Path.GetTempPath(), $"hercules-publish-{Guid.NewGuid():N}{Path.GetExtension(package.FileName)}");
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                await package.CopyToAsync(stream, ct);
            }

            try
            {
                var destPath = _marketplace.Publish(tempPath);
                return Ok(new { path = destPath, fileName = Path.GetFileName(destPath) });
            }
            finally
            {
                if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            }
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Импортировать .skillpkg в локальные навыки (multipart upload).</summary>
    [HttpPost("import")]
    [RequestSizeLimit(52428800)] // 50 MB
    public async Task<ActionResult<SkillMeta>> Import(IFormFile package, [FromQuery] string? conflict, CancellationToken ct)
    {
        if (package is null || package.Length == 0)
        {
            return BadRequest("package is required.");
        }

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"hercules-import-{Guid.NewGuid():N}{Path.GetExtension(package.FileName)}");
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                await package.CopyToAsync(stream, ct);
            }

            try
            {
                var resolution = Enum.TryParse<ConflictResolution>(conflict, ignoreCase: true, out var parsed)
                    ? parsed
                    : ConflictResolution.Rename;
                var skill = _packager.Import(tempPath, resolution);
                return Ok(skill.Meta);
            }
            finally
            {
                if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Импортировать пакет из HTTP URL.</summary>
    [HttpPost("import-url")]
    public async Task<ActionResult> ImportFromUrl([FromBody] ImportUrlRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
        {
            return BadRequest("url is required.");
        }

        try
        {
            var skill = await _marketplace.ImportFromUrlAsync(req.Url, httpClient: null, ct);
            return Ok(skill.Meta);
        }
        catch (HttpRequestException ex)
        {
            return BadRequest($"Failed to download package: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public sealed record InstallRequest(string FileName, ConflictResolution ConflictResolution = ConflictResolution.Rename);
public sealed record ImportUrlRequest(string Url);
