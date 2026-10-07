namespace Hercules.WebApi.Contracts;

/// <summary>Stage 3.5 — scan-only request for the sandbox's dangerous-code rules.</summary>
public sealed class CodeScanRequest
{
    public string Code { get; set; } = "";
}

/// <summary>
/// Result of <c>DangerousCodeScanner</c> over a snippet.
/// <para>
/// Mirrors <c>DangerousCodeScanner.ScanResult</c>, which stops at the first match — so
/// <see cref="Reasons"/> and <see cref="LineNumbers"/> are parallel lists of one entry
/// rather than every violation.
/// </para>
/// </summary>
public sealed class CodeScanResponseDto
{
    public required bool Allowed { get; init; }

    /// <summary>Human-readable reason, e.g. <c>`File.Delete` matched at line 42</c>.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }

    /// <summary>1-based line numbers, parallel to <see cref="Reasons"/>.</summary>
    public required IReadOnlyList<int> LineNumbers { get; init; }
}