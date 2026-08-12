namespace Hercules.Memory.Layers;

/// <summary>
///     Ephemeral per-request context. NOT persisted to disk.
///     Lifetime: single request / turn.
/// </summary>
public sealed class RequestContext : IRequestContext
{
    private readonly List<ConversationTurn> _turns = new();

    public RequestContext(string sessionId)
    {
        SessionId = sessionId;
    }

    public string? CurrentInput { get; set; }
    public string SessionId { get; }

    public IReadOnlyList<ConversationTurn> Turns => _turns.AsReadOnly();

    public void AddTurn(ConversationTurn turn)
    {
        _turns.Add(turn);
    }

    public void Clear()
    {
        _turns.Clear();
        CurrentInput = null;
    }
}
