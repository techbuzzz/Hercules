using Hercules.WebApi.Consensus;
using Hercules.WebApi.Contracts;

namespace Hercules.WebApi.Controllers;

/// <summary>
/// Consensus history (Stage 7.8).
/// <para>
/// A consensus round is the record of what several agents said and which answer was
/// chosen. Losing it on restart would mean an operator cannot review why a decision came
/// out the way it did, so it is persisted agent-side rather than in the browser.
/// </para>
/// </summary>
public static class ConsensusController
{
    public static void MapConsensus(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/consensus").WithTags("Consensus");

        // POST /api/consensus/sessions — record a finished round.
        group.MapPost("/sessions", (SaveConsensusSessionRequest req, ConsensusSessionStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Prompt))
            {
                return Results.BadRequest(new { error = "prompt is required" });
            }

            var session = store.Save(new ConsensusSession(
                Guid.NewGuid().ToString("n"),
                req.Prompt.Trim(),
                req.SelectedAgents,
                req.Responses.Select(r => new ConsensusAnswer(r.AgentName, r.ConnectionId, r.Answer)).ToList(),
                string.IsNullOrWhiteSpace(req.AggregationMode) ? "manual" : req.AggregationMode,
                req.Result,
                req.JudgeRationale,
                DateTimeOffset.UtcNow));

            return Results.Ok(ToDto(session));
        }).WithName("SaveConsensusSession").WithTags("Consensus").Produces<ConsensusSessionDto>(200);

        // GET /api/consensus/sessions — newest first.
        group.MapGet("/sessions", (int? limit, ConsensusSessionStore store) =>
        {
            var items = store.List(limit ?? 50).Select(ToDto).ToList();
            return Results.Ok(new ConsensusSessionListDto { Count = items.Count, Items = items });
        }).WithName("ListConsensusSessions").WithTags("Consensus").Produces<ConsensusSessionListDto>(200);

        // GET /api/consensus/sessions/{id} — one round, read-only.
        group.MapGet("/sessions/{id}", (string id, ConsensusSessionStore store) =>
        {
            var found = store.Get(id);
            return found is null
                ? Results.NotFound(new { error = $"Consensus session '{id}' not found" })
                : Results.Ok(ToDto(found));
        }).WithName("GetConsensusSession").WithTags("Consensus").Produces<ConsensusSessionDto>(200);
    }

    private static ConsensusSessionDto ToDto(ConsensusSession s) => new()
    {
        Id = s.Id,
        Prompt = s.Prompt,
        SelectedAgents = s.SelectedAgents,
        Responses = s.Responses
            .Select(a => new ConsensusAnswerDto { AgentName = a.AgentName, ConnectionId = a.ConnectionId, Answer = a.Answer })
            .ToList(),
        AggregationMode = s.AggregationMode,
        Result = s.Result,
        JudgeRationale = s.JudgeRationale,
        CreatedAt = s.CreatedAt,
    };
}