using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hercules.Config.Rollout;

/// <summary>
///     Локальный валидатор конфигурации/policy перед применением бандла (task_058).
///     Проверяет schema, constraints и совместимость.
/// </summary>
public sealed class LocalConfigValidator
{
    private readonly ILogger<LocalConfigValidator> _logger;
    private readonly ConfigRolloutConfig _config;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public LocalConfigValidator(ConfigRolloutConfig config, ILogger<LocalConfigValidator> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Валидировать payload бандла локально: JSON-парсинг, schema constraints, size limits.
    /// </summary>
    public ValidationResult Validate(ConfigBundle bundle)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. Parse JSON payload
        if (string.IsNullOrWhiteSpace(bundle.Payload))
        {
            errors.Add("Bundle payload is empty.");
            return new ValidationResult(false, errors, warnings);
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(bundle.Payload, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }).RootElement;
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON in payload: {ex.Message}");
            return new ValidationResult(false, errors, warnings);
        }

        // 2. Size check
        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(bundle.Payload);
        if (payloadBytes > _config.MaxPayloadBytes)
        {
            errors.Add($"Payload size {payloadBytes} exceeds maximum {_config.MaxPayloadBytes} bytes.");
        }

        // 3. Type-specific validation
        if (bundle.Type == "config")
        {
            ValidateConfigPayload(root, errors, warnings);
        }
        else if (bundle.Type == "policy")
        {
            ValidatePolicyPayload(root, errors, warnings);
        }
        else
        {
            warnings.Add($"Unknown bundle type '{bundle.Type}'. Skipping type-specific validation.");
        }

        // 4. Stage-specific rules
        if (bundle.Stage == BundleStage.Staging && string.IsNullOrEmpty(bundle.StagingGroup))
        {
            warnings.Add("Bundle is in Staging stage but has no StagingGroup set.");
        }

        // 5. Version format
        if (!Version.TryParse(bundle.Version, out _))
        {
            warnings.Add($"Bundle version '{bundle.Version}' is not a valid semver string.");
        }

        var isValid = errors.Count == 0;
        if (isValid)
        {
            _logger.LogDebug(
                "Local validation passed for bundle {BundleId} v{Version}: {WarningCount} warnings",
                bundle.Id, bundle.Version, warnings.Count);
        }
        else
        {
            _logger.LogWarning(
                "Local validation failed for bundle {BundleId}: {ErrorCount} errors",
                bundle.Id, errors.Count);
        }

        return new ValidationResult(isValid, errors, warnings);
    }

    private void ValidateConfigPayload(JsonElement root, List<string> errors, List<string> warnings)
    {
        // Verify it's an AppConfig-like object (top-level keys)
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Config payload root must be a JSON object.");
            return;
        }

        // Warn if critical top-level sections are missing
        var knownSections = new[] { "llm", "agent", "storage", "mesh" };
        foreach (var section in knownSections)
        {
            if (!root.TryGetProperty(section, out _))
            {
                warnings.Add($"Config payload is missing top-level section '{section}'.");
            }
        }

        // Validate Mesh config structure if present
        if (root.TryGetProperty("mesh", out var mesh) && mesh.ValueKind == JsonValueKind.Object)
        {
            ValidateMeshConfig(mesh, warnings);
        }
    }

    private void ValidatePolicyPayload(JsonElement root, List<string> errors, List<string> warnings)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Policy payload root must be a JSON object.");
            return;
        }

        // Basic policy structure check
        if (!root.TryGetProperty("rules", out _) && !root.TryGetProperty("deny", out _))
        {
            warnings.Add("Policy payload has no 'rules' or 'deny' section.");
        }
    }

    private void ValidateMeshConfig(JsonElement mesh, List<string> warnings)
    {
        // Validate transport section if present
        if (mesh.TryGetProperty("transport", out var transport) && transport.ValueKind == JsonValueKind.String)
        {
            var validTransports = new[] { "in-memory", "http", "redis", "nats" };
            if (!validTransports.Contains(transport.GetString(), StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add($"Unknown mesh transport: '{transport.GetString()}'.");
            }
        }
    }

    public sealed class ValidationResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }

        public ValidationResult(bool isValid, List<string> errors, List<string> warnings)
        {
            IsValid = isValid;
            Errors = errors.AsReadOnly();
            Warnings = warnings.AsReadOnly();
        }
    }
}
