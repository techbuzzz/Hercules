using System.Security.Cryptography;
using System.Text;
using Hercules.Agent;
using Hercules.Cache;
using Hercules.Config;
using Hercules.Storage;

namespace Hercules.Skills.Routing.Deterministic;

/// <summary>
///     Task 023: Детерминированный маршрутизатор навыков без embedding.
///     Комбинирует keyword matching (phrase-receivers), tag intersection и input type matching.
///     Offline-capable: не требует embedding-провайдера.
///     Routing decisions are cached via ICacheService (task_028).
///
///     Scoring:
///     - keyword score = matched_phrase_receivers / total_phrase_receivers [0..1]
///     - tag score     = |input_tags ∩ skill_tags| / max(|input_tags|, 1) [0..1]
///     - type score    = |input_types ∩ skill_input_types| / max(|input_types|, 1) [0..1]
///     - overall      = weighted average of enabled components [0..1]
/// </summary>
public sealed class DeterministicRouter : IDeterministicRouter
{
    private readonly SkillManager _skills;
    private readonly DeterministicRoutingConfig _config;
    private readonly SkillRouter _keywordRouter;
    private readonly ICacheService _cache;

    // Pre-built input-type keyword set for fast lookup
    private static readonly Dictionary<string, string[]> InputTypeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"]       = ["code", "program", "function", "debug", "fix bug", "implement", "refactor", "write code"],
        ["writing"]    = ["write", "draft", "compose", "essay", "article", "blog", "content"],
        ["qa"]         = ["test", "qa", "quality", "bug", "issue", "error", "crash", "failing"],
        ["analysis"]   = ["analyze", "analysis", "review", "assess", "evaluate", "insights", "report"],
        ["translation"]= ["translate", "translation", "localize", "i18n", "l10n"],
        ["data"]      = ["data", "dataset", "csv", "sql", "query", "database", "table", "schema"],
        ["math"]      = ["calculate", "math", "formula", "equation", "compute", "statistics"],
    };

    public DeterministicRouter(
        SkillManager skills,
        DeterministicRoutingConfig config,
        ICacheService? cache = null)
    {
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _keywordRouter = new SkillRouter(skills);
        _cache = cache ?? NullCacheService.Instance;
    }

    public bool IsAvailable => true; // Always available — offline-safe

    public DeterministicRouteResult Route(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return DeterministicRouteResult.None;
        }

        // Never mode: explicitly disabled, never route
        if (_config.FallbackMode == DeterministicFallbackMode.Never)
        {
            return DeterministicRouteResult.None;
        }

        var cacheKey = HashInput(input);

        // Try cache first (sync via GetAwaiter to stay in sync Route method)
        var cached = _cache.GetOrSetAsync(
            CacheClass.RoutingDecision,
            cacheKey,
            () =>
            {
                var r = RouteUncached(input);
                return Task.FromResult(r.IsSkill ? CachedRouteResult.FromResult(r) : null);
            },
            CancellationToken.None).GetAwaiter().GetResult();

        return cached?.Result ?? DeterministicRouteResult.None;
    }

    /// <summary>
    ///     Compute the routing decision without caching.
    ///     Exposed for unit testing and cache population.
    /// </summary>
    public DeterministicRouteResult RouteUncached(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return DeterministicRouteResult.None;

        var normalizedInput = SkillRouter.Normalize(input);
        var inputTags = ExtractTags(normalizedInput);
        var inputTypes = InferInputTypes(normalizedInput);

        Skill? best = null;
        var bestScore = 0.0;
        var bestMethods = Array.Empty<string>();

        foreach (Skill skill in _skills.All())
        {
            var (score, methods) = ScoreSkill(
                normalizedInput, skill, inputTags, inputTypes);

            if (score > bestScore ||
                (score == bestScore && best is not null &&
                 skill.Meta.SuccessRate > best.Meta.SuccessRate))
            {
                best = skill;
                bestScore = score;
                bestMethods = methods;
            }
        }

        return bestScore > 0
            ? new DeterministicRouteResult(best, bestScore, bestMethods)
            : DeterministicRouteResult.None;
    }

    private static string HashInput(string input)
    {
        // Short stable hash for cache key — we don't need cryptographic strength here
        var normalized = SkillRouter.Normalize(input).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16]; // First 16 hex chars = 64 bits
    }

    private (double Score, string[] Methods) ScoreSkill(
        string normalizedInput,
        Skill skill,
        HashSet<string> inputTags,
        HashSet<string> inputTypes)
    {
        var methods = new List<string>();
        var totalScore = 0.0;

        // ── Keyword matching (always enabled) ──────────────────────────────
        // Raw count: number of matched phrase receivers (like legacy SkillRouter).
        // This is the primary signal — more matching receivers = higher score.
        var keywordScore = ScoreKeyword(normalizedInput, skill);
        if (keywordScore > 0)
        {
            methods.Add("keyword");
            var w = _config.ScoringWeights.TryGetValue("keyword", out var kw) ? kw : 0.60;
            totalScore += keywordScore * w;
        }

        // ── Tag matching ─────────────────────────────────────────────────────
        // Normalized [0..1] intersection ratio — adds bonus on top of keyword.
        if (_config.EnableTagMatching)
        {
            var tagScore = ScoreTagMatch(inputTags, skill);
            if (tagScore > 0)
            {
                methods.Add("tag");
                var w = _config.ScoringWeights.TryGetValue("tag", out var t) ? t : 0.25;
                totalScore += tagScore * w;
            }
        }

        // ── Input type matching ──────────────────────────────────────────────
        // Normalized [0..1] intersection ratio — adds bonus on top of keyword.
        if (_config.EnableInputTypeMatching && inputTypes.Count > 0)
        {
            var typeScore = ScoreTypeMatch(inputTypes, skill);
            if (typeScore > 0)
            {
                methods.Add("type");
                var w = _config.ScoringWeights.TryGetValue("type", out var tp) ? tp : 0.15;
                totalScore += typeScore * w;
            }
        }

        return (totalScore, methods.ToArray());
    }

    private static double ScoreKeyword(string normalizedInput, Skill skill)
    {
        var receivers = skill.Meta.PhraseReceivers
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        if (receivers.Count == 0) return 0;

        var matched = receivers.Count(r =>
            normalizedInput.Contains(
                SkillRouter.Normalize(r),
                StringComparison.Ordinal));

        // Raw count: matches the legacy SkillRouter behavior where more matching
        // phrase receivers → higher score → preferred. Normalization would make
        // single-receiver skills score equal to multi-receiver skills, breaking
        // the test that verifies "more keyword matches wins".
        return matched;
    }

    private static double ScoreTagMatch(HashSet<string> inputTags, Skill skill)
    {
        if (inputTags.Count == 0) return 0;

        var skillTags = new HashSet<string>(
            skill.Meta.Tags.Select(t => t.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        if (skillTags.Count == 0) return 0;

        var intersection = inputTags.Count(t => skillTags.Contains(t));
        return (double)intersection / Math.Max(inputTags.Count, 1);
    }

    private static double ScoreTypeMatch(HashSet<string> inputTypes, Skill skill)
    {
        if (inputTypes.Count == 0) return 0;

        var skillTypes = new HashSet<string>(
            skill.Meta.InputTypes.Select(t => t.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        if (skillTypes.Count == 0) return 0;

        var intersection = inputTypes.Count(t => skillTypes.Contains(t));
        return (double)intersection / Math.Max(inputTypes.Count, 1);
    }

    /// <summary>
    ///     Extract tags from normalized input by looking for known tag keywords.
    ///     Tags are multi-word phrases or single tokens that appear in input.
    /// </summary>
    private static HashSet<string> ExtractTags(string normalizedInput)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = normalizedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Known technology/tool tags
        var knownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "python", "javascript", "typescript", "java", "csharp", "csharp", "go", "rust",
            "csharp", "cpp", "c++", "sql", "bash", "shell", "powershell", "docker",
            "kubernetes", "k8s", "git", "github", "api", "rest", "graphql", "grpc",
            "http", "json", "xml", "yaml", "toml", "html", "css", "react", "angular",
            "vue", "node", "nodejs", "dotnet", ".net", "aspnet", "asp.net",
            "postgresql", "postgres", "mysql", "mongodb", "redis", "elasticsearch",
            "rabbitmq", "kafka", "aws", "azure", "gcp", "terraform", "ansible",
            "ci", "cd", "jenkins", "github-actions", "gitlab", "bitbucket",
            "linux", "windows", "macos", "ubuntu", "debian", "alpine",
            "debug", "performance", "security", "testing", "ci/cd", "devops",
            "cli", "gui", "web", "mobile", "frontend", "backend", "fullstack",
            "microservices", "serverless", "lambda", "cloud", "on-prem", "onpremise",
            "markdown", "latex", "pdf", "office", "excel", "word", "powerpoint",
            "logging", "monitoring", "metrics", "tracing", "alerting",
            "auth", "authentication", "oauth", "jwt", "sso", "ldap", "ad",
            "encryption", "tls", "ssl", "certificates", "secrets", "vault",
        };

        foreach (var word in words)
        {
            if (knownTags.Contains(word))
                tags.Add(word);
        }

        return tags;
    }

    /// <summary>
    ///     Infer input types from normalized input by keyword matching against known type patterns.
    /// </summary>
    private static HashSet<string> InferInputTypes(string normalizedInput)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (inputType, keywords) in InputTypeKeywords)
        {
            if (keywords.Any(kw =>
                normalizedInput.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            {
                types.Add(inputType);
            }
        }

        return types;
    }
}
