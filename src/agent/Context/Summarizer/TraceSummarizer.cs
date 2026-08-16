using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace Hercules.Context.Summarizer;

/// <summary>
///     Summarizes tool traces into episodic memory entries.
///     Groups by tool, aggregates results, produces a compact summary.
///
///     Task 027 — Context Assembly.
///     <para>
///         task_084: per-instance <see cref="StringBuilder" /> pool to reduce
///         allocations when summaries are produced after every multi-tool turn.
///     </para>
/// </summary>
public sealed class TraceSummarizer : ITraceSummarizer
{
    // task_084: pooled StringBuilder for hot summarization path.
    private static readonly ObjectPool<StringBuilder> SbPool =
        new DefaultObjectPoolProvider().CreateStringBuilderPool();

    /// <inheritdoc />
    public string Summarize(IReadOnlyList<ToolTraceEntry> trace)
    {
        if (trace.Count == 0)
            return "[Empty trace] No tool calls recorded.";

        // task_084: pool the StringBuilder.
        var sb = SbPool.Get();
        try
        {
            sb.Append($"[Tool trace summary] {trace.Count} tool call(s): ");

            // Group by tool name
            var groups = trace
                .GroupBy(t => t.ToolName)
                .OrderByDescending(g => g.Count())
                .ToList();

            var parts = new List<string>();
            foreach (var group in groups)
            {
                var calls = group.ToList();
                var successes = calls.Count(c => c.Success);
                var failures = calls.Count - successes;
                var totalMs = calls.Sum(c => c.DurationMs);
                var avgMs = totalMs / calls.Count;

                // Capture first non-empty output snippet (for single calls)
                string outputSnippet = "";
                if (calls.Count == 1 && !string.IsNullOrWhiteSpace(calls[0].Output))
                {
                    outputSnippet = Truncate(calls[0].Output, 60);
                }

                var callDesc = $"{group.Key} ({calls.Count}x, {avgMs}ms avg, {successes}ok/{failures}fail)";
                if (!string.IsNullOrEmpty(outputSnippet))
                    callDesc += $" → {outputSnippet}";
                parts.Add(callDesc);
            }

            sb.Append(string.Join("; ", parts));
            sb.Append('.');

            return sb.ToString();
        }
        finally
        {
            SbPool.Return(sb);
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
