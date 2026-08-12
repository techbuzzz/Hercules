using Hercules.Agent;
using Hercules.Skills;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты манифеста навыка: получение, валидация совместимости.
/// </summary>
public static class SkillManifestController
{
    public static void MapSkillManifest(this IEndpointRouteBuilder app)
    {
        // GET /api/skills/{id}/manifest — получить манифест навыка
        app.MapGet("/api/skills/{id}/manifest", (string id, SkillManager manager) =>
        {
            var skill = manager.Get(id);
            if (skill is null)
            {
                return Results.NotFound(new { error = $"Навык '{id}' не найден." });
            }

            var manifest = ToManifest(skill);
            return Results.Ok(manifest);
        }).WithName("GetSkillManifest");

        // POST /api/skills/{id}/manifest/validate — валидировать совместимость манифеста
        app.MapPost("/api/skills/{id}/manifest/validate", (string id, SkillManager manager,
            SkillManifestValidator validator, IReadOnlyList<string>? knownTools) =>
        {
            var skill = manager.Get(id);
            if (skill is null)
            {
                return Results.NotFound(new { error = $"Навык '{id}' не найден." });
            }

            var manifest = ToManifest(skill);

            // Override known tools if provided in request body
            var effectiveValidator = knownTools is { Count: > 0 }
                ? new SkillManifestValidator(
                    validator.CurrentHerculesVersion,
                    new HashSet<string>(knownTools, StringComparer.OrdinalIgnoreCase),
                    validator.AllowedRiskLevels)
                : validator;

            var result = effectiveValidator.Validate(manifest);
            return Results.Ok(new
            {
                skillId = id,
                skillName = skill.Meta.Name,
                isValid = result.IsValid,
                errors = result.Errors,
                warnings = result.Warnings,
                missingTools = result.MissingTools,
                isCompatible = result.IsCompatible,
                validatedAt = DateTime.UtcNow.ToString("o")
            });
        }).WithName("ValidateSkillManifest");

        // POST /api/skills/manifest/validate-all — валидировать все навыки
        app.MapPost("/api/skills/manifest/validate-all", (SkillManager manager,
            SkillManifestValidator validator) =>
        {
            var skills = manager.All();
            var results = new List<object>();
            foreach (var skill in skills)
            {
                var manifest = ToManifest(skill);
                var result = validator.Validate(manifest);
                results.Add(new
                {
                    skillId = skill.Meta.Id,
                    skillName = skill.Meta.Name,
                    isValid = result.IsValid,
                    errors = result.Errors,
                    missingTools = result.MissingTools,
                    isCompatible = result.IsCompatible
                });
            }

            return Results.Ok(new
            {
                total = results.Count,
                validatedAt = DateTime.UtcNow.ToString("o"),
                results
            });
        }).WithName("ValidateAllSkillManifests");
    }

    private static SkillManifest ToManifest(Storage.Skill skill)
    {
        return new SkillManifest
        {
            SchemaVersion = "1.0.0",
            Owner = skill.Meta.Owner,
            MinHerculesVersion = skill.Meta.MinHerculesVersion,
            MaxHerculesVersion = skill.Meta.MaxHerculesVersion,
            InputSchemaVersion = skill.Meta.InputSchemaVersion,
            OutputSchemaVersion = skill.Meta.OutputSchemaVersion,
            RequiredTools = skill.Meta.Tools?.Select(t => t.Name).ToList() ?? new List<string>(),
            Permissions = skill.Meta.Permissions,
            ModelRequirements = skill.Meta.ModelRequirements,
            RiskLevel = (SkillRiskLevel)skill.Meta.RiskLevel,
            Budget = skill.Meta.Budget is not null
                ? new SkillManifestBudget
                {
                    MaxTokensPerCall = skill.Meta.Budget.MaxTokensPerCall,
                    MaxCallsPerMinute = skill.Meta.Budget.MaxCallsPerMinute,
                    MaxCostPerCallUsd = skill.Meta.Budget.MaxCostPerCallUsd
                }
                : null
        };
    }
}
