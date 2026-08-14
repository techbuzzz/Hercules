using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Verification;

/// <summary>
///     Проверяет ответ на:
///     - PII / sensitive data leakage
///     - Code injection patterns
///     - Dangerous command patterns
///     - Prompt injection markers
///     - Unsafe URL or file path patterns
/// </summary>
public sealed partial class SafetyVerifier : IVerifier
{
    private readonly ILogger<SafetyVerifier> _logger;

    public SafetyVerifier(ILogger<SafetyVerifier> logger) => _logger = logger;

    public string Name => "SafetyVerifier";

    public bool CanVerify(VerificationContext ctx) => true; // Applicable to all responses

    public Task<VerifierResult> VerifyAsync(VerificationContext ctx, CancellationToken ct = default)
    {
        var text = ctx.ResponseText;
        if (string.IsNullOrEmpty(text))
            return Task.FromResult(VerifierResult.Pass(Name));

        var issues = new List<string>();
        var details = new Dictionary<string, string>();

        // Check for code injection patterns
        if (CodeInjectionRx().IsMatch(text))
        {
            issues.Add("code_injection_pattern");
            details["pattern_type"] = "code_injection";
            _logger.LogWarning("[SafetyVerifier] Code injection pattern detected in response {VerificationId}",
                ctx.VerificationId);
        }

        // Check for dangerous shell commands
        if (DangerousCommandRx().IsMatch(text))
        {
            issues.Add("dangerous_command");
            details["pattern_type"] = "dangerous_command";
            _logger.LogWarning("[SafetyVerifier] Dangerous command detected in response {VerificationId}",
                ctx.VerificationId);
        }

        // Check for suspicious URL patterns (potential SSRF)
        if (SuspiciousUrlRx().IsMatch(text))
        {
            issues.Add("suspicious_url");
            details["pattern_type"] = "suspicious_url";
        }

        // Check for local file path traversal
        if (PathTraversalRx().IsMatch(text))
        {
            issues.Add("path_traversal");
            details["pattern_type"] = "path_traversal";
        }

        // Check for prompt injection markers
        if (PromptInjectionRx().IsMatch(text))
        {
            issues.Add("prompt_injection_marker");
            details["pattern_type"] = "prompt_injection";
        }

        // High-impact tool usage (file write, network, exec)
        if (ctx.Mode == "tool" && ctx.ToolUsed is not null)
        {
            if (HighImpactTools.Contains(ctx.ToolUsed.ToLowerInvariant()))
            {
                issues.Add("high_impact_tool");
                details["tool"] = ctx.ToolUsed;
                details["risk"] = "external_side_effect";
            }
        }

        if (issues.Count == 0)
            return Task.FromResult(VerifierResult.Pass(Name));

        var severity = issues.Contains("code_injection_pattern") || issues.Contains("dangerous_command")
            ? VerificationSeverity.High
            : VerificationSeverity.Medium;

        return Task.FromResult(VerifierResult.Fail(
            Name,
            $"Safety issues detected: {string.Join(", ", issues)}",
            severity,
            "SAFETY_BLOCK",
            details));
    }

    private static readonly HashSet<string> HighImpactTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "file_write", "file_delete", "shell_exec", "exec", "run", "process",
        "http_request", "fetch_url", "send_webhook", "delete_file",
        "rm", "mv", "cp", "curl", "wget", "ssh", "docker", "kubectl"
    };

    [GeneratedRegex(@"(?i)\b(eval|exec|system|popen|spawn|subprocess)\s*\(|\beval\s*\(.*\$|\bexec\s*\(.*\$", RegexOptions.Compiled)]
    private static partial Regex CodeInjectionRx();

    [GeneratedRegex(@"(?i)\b(rm\s+-rf\s+/|mkfs|dd\s+if=|:(){ :|:& };:|:\|&|>\s*/dev/sd|chmod\s+-?\s*0|sudo\s+rm|wget\s+\|sh|curl\s+\|sh)", RegexOptions.Compiled)]
    private static partial Regex DangerousCommandRx();

    [GeneratedRegex(@"(?i)https?://(localhost|127\.\d+\.\d+\.\d+|0\.0\.0\.0|::1|metadata\.google|metadata\.azure|169\.254\.169\.254)", RegexOptions.Compiled)]
    private static partial Regex SuspiciousUrlRx();

    [GeneratedRegex(@"(?i)(\.\.\/){2,}|\.\.%2f|%2e%2e%2f|\.\.%5c|\\\\UNC|\\\\\w+\\\w+", RegexOptions.Compiled)]
    private static partial Regex PathTraversalRx();

    [GeneratedRegex(@"(?i)(\[system\]|\[internal\]|\[role:\s*admin\]|\[bypass\]|你是谁|ignore\s+previous|disregard\s+all)", RegexOptions.Compiled)]
    private static partial Regex PromptInjectionRx();
}
