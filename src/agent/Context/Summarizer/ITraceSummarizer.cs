namespace Hercules.Context.Summarizer;

/// <summary>
///     Interface for tool trace summarization.
///     Compresses completed tool traces into episodic memory entries.
/// </summary>
public interface ITraceSummarizer
{
    /// <summary>
    ///     Summarize a tool trace into a human-readable string for episodic storage.
    /// </summary>
    /// <param name="trace">List of tool call entries.</param>
    /// <returns>Summary string suitable for episodic memory.</returns>
    string Summarize(IReadOnlyList<ToolTraceEntry> trace);
}
