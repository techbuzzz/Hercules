namespace Hercules.Memory.Layers;

/// <summary>
///     Ephemeral per-request context. NOT persisted to disk.
///     Lifetime: single request / turn.
/// </summary>
public interface IRequestContext
{
    /// <summary>Current user input for this request.</summary>
    string? CurrentInput { get; set; }

    /// <summary>Session ID this request belongs to.</summary>
    string SessionId { get; }

    /// <summary>Conversation turns accumulated in this request (system turns excluded).</summary>
    IReadOnlyList<ConversationTurn> Turns { get; }

    /// <summary>Append a turn to the current request context.</summary>
    void AddTurn(ConversationTurn turn);

    /// <summary>Clear all turns (end of request).</summary>
    void Clear();
}

public sealed record ConversationTurn(string Role, string Content, DateTime At);
