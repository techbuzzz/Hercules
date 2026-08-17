using Hercules.Skills;
using Hercules.Skills.Marketplace;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI маршруты маркетплейса навыков (task_021, task_112).
///     CRUD для пакетов, верификация, импорт по URL.
///     Делегирует в <see cref="SkillMarketplace"/> и <see cref="SkillPackager"/>.
/// </summary>
public static class MarketplaceController
{
    public static void MapMarketplace(this IEndpointRouteBuilder app)
    {
        // GET /api/marketplace — список пакетов в маркетплейсе
        app.MapGet("/api/marketplace", (SkillMarketplace marketplace) =>
                Results.Ok(marketplace.List()))
            .WithName("ListMarketplace");

        // GET /api/marketplace/search — поиск пакетов по запросу
        app.MapGet("/api/marketplace/search", (SkillMarketplace marketplace, string? q) =>
        {
            var list = string.IsNullOrWhiteSpace(q) ? marketplace.List() : marketplace.Search(q);
            return Results.Ok(list);
        }).WithName("SearchMarketplace");

        // GET /api/marketplace/{file}/verify — проверить integrity (hash + signature) пакета
        app.MapGet("/api/marketplace/{file}/verify", (SkillMarketplace marketplace, string file) =>
        {
            var result = marketplace.VerifyPackage(file);
            return !result.IsValid && result.Error is not null
                ? Results.BadRequest(result)
                : Results.Ok(result);
        }).WithName("VerifyMarketplacePackage");

        // GET /api/marketplace/{file}/deps — зависимости пакета
        app.MapGet("/api/marketplace/{file}/deps", (SkillMarketplace marketplace, string file) =>
                Results.Ok(marketplace.GetDependencies(file)))
            .WithName("GetMarketplacePackageDeps");

        // POST /api/marketplace/install — установить пакет из маркетплейса
        app.MapPost("/api/marketplace/install", (SkillMarketplace marketplace, InstallRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.FileName))
            {
                return Results.BadRequest(new { error = "fileName is required." });
            }

            try
            {
                var skill = marketplace.Install(req.FileName, req.ConflictResolution);
                return Results.Ok(skill.Meta);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("InstallMarketplacePackage");

        // POST /api/marketplace/install-with-deps — установить пакет со всеми зависимостями
        app.MapPost("/api/marketplace/install-with-deps", (SkillMarketplace marketplace, InstallRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.FileName))
            {
                return Results.BadRequest(new { error = "fileName is required." });
            }

            try
            {
                var skills = marketplace.InstallWithDeps(req.FileName);
                return Results.Ok(skills.Select(s => s.Meta).ToList());
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("InstallMarketplacePackageWithDeps");

        // DELETE /api/marketplace/{file} — удалить пакет из маркетплейса
        app.MapDelete("/api/marketplace/{file}", (SkillMarketplace marketplace, string file) =>
        {
            var removed = marketplace.Remove(file);
            return !removed
                ? Results.NotFound(new { error = $"Package '{file}' not found in marketplace." })
                : Results.NoContent();
        }).WithName("DeleteMarketplacePackage");

        // POST /api/marketplace/publish — опубликовать .skillpkg в маркетплейс (multipart upload)
        app.MapPost("/api/marketplace/publish", async (
                HttpContext ctx,
                SkillMarketplace marketplace,
                CancellationToken ct) =>
            {
                var form = await ctx.Request.ReadFormAsync(ct);
                var package = form.Files.GetFile("package");
                if (package is null || package.Length == 0)
                {
                    return Results.BadRequest(new { error = "package is required." });
                }

                try
                {
                    var tempPath = Path.Combine(
                        Path.GetTempPath(),
                        $"hercules-publish-{Guid.NewGuid():N}{Path.GetExtension(package.FileName)}");
                    await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    {
                        await package.CopyToAsync(stream, ct);
                    }

                    try
                    {
                        var destPath = marketplace.Publish(tempPath);
                        return Results.Ok(new { path = destPath, fileName = Path.GetFileName(destPath) });
                    }
                    finally
                    {
                        if (System.IO.File.Exists(tempPath))
                        {
                            System.IO.File.Delete(tempPath);
                        }
                    }
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new { error = ex.Message });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("PublishMarketplacePackage")
            .DisableAntiforgery();

        // POST /api/marketplace/import — импортировать .skillpkg в локальные навыки (multipart upload)
        app.MapPost("/api/marketplace/import", async (
                HttpContext ctx,
                SkillPackager packager,
                string? conflict,
                CancellationToken ct) =>
            {
                var form = await ctx.Request.ReadFormAsync(ct);
                var package = form.Files.GetFile("package");
                if (package is null || package.Length == 0)
                {
                    return Results.BadRequest(new { error = "package is required." });
                }

                try
                {
                    var tempPath = Path.Combine(
                        Path.GetTempPath(),
                        $"hercules-import-{Guid.NewGuid():N}{Path.GetExtension(package.FileName)}");
                    await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    {
                        await package.CopyToAsync(stream, ct);
                    }

                    try
                    {
                        var resolution = Enum.TryParse<ConflictResolution>(conflict, ignoreCase: true, out var parsed)
                            ? parsed
                            : ConflictResolution.Rename;
                        var skill = packager.Import(tempPath, resolution);
                        return Results.Ok(skill.Meta);
                    }
                    finally
                    {
                        if (System.IO.File.Exists(tempPath))
                        {
                            System.IO.File.Delete(tempPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("ImportMarketplacePackage")
            .DisableAntiforgery();

        // POST /api/marketplace/import-url — импортировать пакет из HTTP URL
        app.MapPost("/api/marketplace/import-url", async (
                SkillMarketplace marketplace,
                ImportUrlRequest req,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.Url))
                {
                    return Results.BadRequest(new { error = "url is required." });
                }

                try
                {
                    var skill = await marketplace.ImportFromUrlAsync(req.Url, httpClient: null, ct);
                    return Results.Ok(skill.Meta);
                }
                catch (HttpRequestException ex)
                {
                    return Results.BadRequest(new { error = $"Failed to download package: {ex.Message}" });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("ImportMarketplacePackageFromUrl");
    }
}

public sealed record InstallRequest(string FileName, ConflictResolution ConflictResolution = ConflictResolution.Rename);
public sealed record ImportUrlRequest(string Url);
