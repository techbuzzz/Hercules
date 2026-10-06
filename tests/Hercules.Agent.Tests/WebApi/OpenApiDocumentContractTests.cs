using System.Text.Json;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     Contract tests over the committed <c>openapi.json</c>.
/// </summary>
/// <remarks>
///     R7/R41/R42: no test asserted ANYTHING about this document. That is precisely how a
///     document with no security scheme, two operations missing <c>operationId</c>, and an
///     orphan tag shipped despite the recent OpenAPI work (task_109/110/111). These tests
///     are the regression gate that was missing.
/// </remarks>
public class OpenApiDocumentContractTests
{
    // Copied next to the test binaries by the csproj, so this asserts against the COMMITTED
    // artefact rather than something regenerated during the test run.
private static readonly string DocumentPath = Path.Combine(AppContext.BaseDirectory, "openapi.json");

private static JsonDocument Parse() => JsonDocument.Parse(File.ReadAllText(DocumentPath));

private static IEnumerable<(string Path, JsonElement Operation)> Operations()
    {
        using var doc = Parse();
        var paths = doc.RootElement.GetProperty("paths");

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.Name is "get" or "post" or "put" or "patch" or "delete")
                {
                    yield return (path.Name, method.Value);
                }
            }
        }
    }

    [Fact]
    public void Document_IsNonEmpty()
    {
        using var doc = Parse();

        var paths = doc.RootElement.GetProperty("paths");
        var count = paths.EnumerateObject().Count();

        // A regression to `paths: {}` is the task_109 failure mode; keep a floor that would
        // catch an accidental regeneration against an unstarted host.
        Assert.True(count >= 200, $"Expected the full API surface, found only {count} paths.");
    }

    [Fact]
    public void EveryOperation_HasAtLeastOneTag()
    {
        var untagged = Operations()
            .Where(o => !o.Operation.TryGetProperty("tags", out var tags) || tags.GetArrayLength() == 0)
            .Select(o => o.Path)
            .ToList();

        Assert.True(
            untagged.Count == 0,
            "Operations with no tag (Orval tags-split drops them into a default bucket): "
            + string.Join(", ", untagged));
    }

    [Fact]
    public void EveryOperation_HasUniqueOperationId()
    {
        var missing = Operations()
            .Where(o => !o.Operation.TryGetProperty("operationId", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
            .Select(o => o.Path)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Operations missing operationId (Orval generates invalid/duplicate client methods): "
            + string.Join(", ", missing));

        var duplicates = Operations()
            .Select(o => o.Operation.GetProperty("operationId").GetString())
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, "Duplicate operationIds: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void ApiKeySecurityScheme_IsDeclared()
    {
        using var doc = Parse();

        // R7: ApiKeyMiddleware gates every /api route, but the document declared no
        // security scheme, so generated clients sent no header and every call 401'd.
        var schemes = doc.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");

        Assert.True(schemes.TryGetProperty("ApiKey", out var scheme), "components.securitySchemes.ApiKey is missing.");

        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
    }

    [Fact]
    public void ProtectedApiOperations_DeclareSecurity()
    {
        var unprotected = Operations()
            .Where(o => o.Path.StartsWith("/api/", StringComparison.Ordinal)
                        && !o.Path.StartsWith("/api/health", StringComparison.Ordinal)
                        && !o.Path.StartsWith("/api/ready", StringComparison.Ordinal)
                        && !(o.Operation.TryGetProperty("security", out _)))
            .Select(o => o.Path)
            .Distinct()
            .ToList();

        Assert.True(
            unprotected.Count == 0,
            "Protected /api operations without a security requirement: " + string.Join(", ", unprotected));
    }

    [Fact]
    public void HealthOperations_AreNotSecurityGated()
    {
        // Liveness/readiness probes run without an API key by design — ApiKeyMiddleware
        // bypasses them, so the document must not demand a key there.
        var gated = Operations()
            .Where(o => (o.Path.StartsWith("/api/health", StringComparison.Ordinal)
                         || o.Path.StartsWith("/api/ready", StringComparison.Ordinal))
                        && o.Operation.TryGetProperty("security", out _))
            .Select(o => o.Path)
            .ToList();

        Assert.True(gated.Count == 0, "Health endpoints must not require an API key: " + string.Join(", ", gated));
    }
}