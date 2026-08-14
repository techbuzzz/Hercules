using Hercules.Mesh.A2A;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     A2A Agent Card endpoints: publish, discover, import remote cards.
/// </summary>
public static class A2AController
{
    public static void MapA2A(this IEndpointRouteBuilder app)
    {
        // GET /api/a2a/agent-card — получить локальный A2A Agent Card.
        app.MapGet("/api/a2a/agent-card", async (IAgentCardService agentCardService, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.AgentCard");
            try
            {
                var card = await agentCardService.GetAgentCardAsync(ct);
                return Results.Ok(card);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to generate Agent Card");
                return Results.StatusCode(500);
            }
        }).WithName("GetAgentCard");

        // GET /api/a2a/agent-card/from?url=xxx — импортировать remote Agent Card.
        app.MapGet("/api/a2a/agent-card/from", async (string url, IAgentCardService agentCardService, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return Results.BadRequest(new { error = "url query parameter is required" });
            }

            var logger = loggerFactory.CreateLogger("A2A.AgentCard");
            try
            {
                var card = await agentCardService.ImportFromUrlAsync(url, ct);
                return Results.Ok(card);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("Failed to import Agent Card from {Url}: {Message}", url, ex.Message);
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error importing Agent Card from {Url}", url);
                return Results.StatusCode(500);
            }
        }).WithName("ImportAgentCardFromUrl");

        // POST /api/a2a/discover — discover Agent Cards из списка URLs.
        // Body: array of URLs, e.g. ["https://peer1.com/agent-card.json", "..."]
        app.MapPost("/api/a2a/discover", async (List<string> urls, IAgentCardService agentCardService, CancellationToken ct) =>
        {
            if (urls is null || urls.Count == 0)
            {
                return Results.BadRequest(new { error = "urls array is required and must not be empty" });
            }

            var results = await agentCardService.DiscoverAsync(urls, ct);
            return Results.Ok(new
            {
                total = urls.Count,
                discovered = results.Count,
                failed = urls.Count - results.Count,
                cards = results.Select(r => new
                {
                    url = r.Url,
                    name = r.Card.Name,
                    version = r.Card.Version,
                    url2 = r.Card.Url,
                    skills = r.Card.Skills.Select(s => new { s.Id, s.Name }).ToList()
                }).ToList()
            });
        }).WithName("DiscoverAgentCards");

        // POST /api/a2a/agent-card/publish — принудительно опубликовать локальный Agent Card.
        app.MapPost("/api/a2a/agent-card/publish", async (IAgentCardService agentCardService, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.AgentCard");
            try
            {
                string? path = await agentCardService.PublishAsync(ct);
                if (string.IsNullOrEmpty(path))
                {
                    return Results.Ok(new { status = "disabled", message = "Agent Card publishing is disabled in config" });
                }

                return Results.Ok(new { status = "published", path });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish Agent Card");
                return Results.StatusCode(500);
            }
        }).WithName("PublishAgentCard");

        // GET /api/a2a/agent-card/fresh — проверить freshness кэша Agent Card.
        app.MapGet("/api/a2a/agent-card/fresh", (IAgentCardService agentCardService) =>
        {
            bool isCurrent = agentCardService.IsCurrent();
            return Results.Ok(new { isCurrent });
        }).WithName("AgentCardIsFresh");
    }
}
