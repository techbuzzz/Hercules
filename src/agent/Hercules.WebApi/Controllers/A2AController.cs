using Hercules.Mesh.A2A;
using Microsoft.AspNetCore.Mvc;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     A2A Agent Card endpoints: publish, discover, import remote cards.
/// </summary>
[ApiController]
[Route("api/a2a")]
public sealed class A2AController : ControllerBase
{
    private readonly IAgentCardService _agentCardService;
    private readonly ILogger<A2AController> _logger;

    public A2AController(IAgentCardService agentCardService, ILogger<A2AController> logger)
    {
        _agentCardService = agentCardService ?? throw new ArgumentNullException(nameof(agentCardService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     GET /api/a2a/agent-card — получить локальный A2A Agent Card.
    /// </summary>
    [HttpGet("agent-card")]
    public async Task<IActionResult> GetAgentCard(CancellationToken ct)
    {
        try
        {
            var card = await _agentCardService.GetAgentCardAsync(ct);
            return Ok(card);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Agent Card");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    ///     GET /api/a2a/agent-card?url=xxx — импортировать remote Agent Card.
    /// </summary>
    [HttpGet("agent-card/from")]
    public async Task<IActionResult> ImportFromUrl([FromQuery] string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest(new { error = "url query parameter is required" });

        try
        {
            var card = await _agentCardService.ImportFromUrlAsync(url, ct);
            return Ok(card);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Failed to import Agent Card from {Url}: {Message}", url, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error importing Agent Card from {Url}", url);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    ///     POST /api/a2a/discover — discover Agent Cards из списка URLs.
    ///     Body: array of URLs, e.g. ["https://peer1.com/agent-card.json", "..."]
    /// </summary>
    [HttpPost("discover")]
    public async Task<IActionResult> Discover([FromBody] List<string> urls, CancellationToken ct)
    {
        if (urls is null || urls.Count == 0)
            return BadRequest(new { error = "urls array is required and must not be empty" });

        var results = await _agentCardService.DiscoverAsync(urls, ct);
        return Ok(new
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
    }

    /// <summary>
    ///     POST /api/a2a/agent-card/publish — принудительно опубликовать локальный Agent Card.
    /// </summary>
    [HttpPost("agent-card/publish")]
    public async Task<IActionResult> Publish(CancellationToken ct)
    {
        try
        {
            string path = await _agentCardService.PublishAsync(ct);
            if (string.IsNullOrEmpty(path))
                return Ok(new { status = "disabled", message = "Agent Card publishing is disabled in config" });

            return Ok(new { status = "published", path });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish Agent Card");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    ///     GET /api/a2a/agent-card/fresh — проверить freshness кэша Agent Card.
    /// </summary>
    [HttpGet("agent-card/fresh")]
    public IActionResult IsFresh()
    {
        bool isCurrent = _agentCardService.IsCurrent();
        return Ok(new { isCurrent });
    }
}
