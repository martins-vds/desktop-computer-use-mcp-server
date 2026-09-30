using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Applications;

public static class ApplicationProfileValidator
{
    public static void Validate(ApplicationProfile profile)
    {
        ValidateIdentity(profile);
        ValidateLimits(profile);
        ValidateLegacySelectors(profile);
        ValidateSchema(profile);
    }

    private static void ValidateIdentity(ApplicationProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            throw new ProfileValidationException("Application profile ID is required.");
        }

        if (profile.Id.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ProfileValidationException(
                $"Application profile '{profile.Id}' contains unsupported characters.");
        }

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            throw new ProfileValidationException(
                $"Application profile '{profile.Id}' requires a display name.");
        }

        if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            throw new ProfileValidationException(
                $"Application profile '{profile.Id}' requires an executable path.");
        }

        if (!Path.IsPathFullyQualified(profile.ExecutablePath))
        {
            throw new ProfileValidationException(
                $"Application profile '{profile.Id}' must resolve to an absolute executable path.");
        }
    }

    private static void ValidateLimits(ApplicationProfile profile)
    {
        if (profile.OperationTimeoutMs is < 100 or > 300_000)
        {
            throw new ProfileValidationException(
                $"Application profile '{profile.Id}' operation timeout must be between 100 and 300000 milliseconds.");
        }

        if (profile.PollIntervalMs is < 10 or > 10_000)
        {
            throw new ProfileValidationException(
                $"Application profile '{profile.Id}' poll interval must be between 10 and 10000 milliseconds.");
        }

        if (profile.MaxTreeDepth is < 1 or > 20)
        {
            throw new ProfileValidationException(
                $"Application profile '{profile.Id}' max tree depth must be between 1 and 20.");
        }

        if (profile.MaxResults is < 1 or > 5_000)
        {
            throw new ProfileValidationException(
                $"Application profile '{profile.Id}' max results must be between 1 and 5000.");
        }
    }

    private static void ValidateLegacySelectors(ApplicationProfile profile)
    {
        foreach (var (key, selector) in profile.SemanticSelectors)
        {
            if (string.IsNullOrWhiteSpace(key) || selector.IsEmpty)
            {
                throw new ProfileValidationException(
                    $"Application profile '{profile.Id}' contains an invalid semantic selector.");
            }

            if (!string.IsNullOrWhiteSpace(selector.SemanticKey))
            {
                throw new ProfileValidationException(
                    $"Semantic selector '{key}' in profile '{profile.Id}' cannot reference another semantic key.");
            }

            if (selector.Index is < 0)
            {
                throw new ProfileValidationException(
                    $"Semantic selector '{key}' in profile '{profile.Id}' has a negative index.");
            }
        }
    }

    private static void ValidateSchema(ApplicationProfile profile)
    {
        if (profile.SchemaVersion is < 1 or > 2)
        {
            throw new ProfileValidationException(
                $"Application profile '{profile.Id}' has unsupported schema version {profile.SchemaVersion}.");
        }

        foreach (var (key, target) in profile.SemanticTargets)
        {
            ValidateSemanticTarget(profile.Id, key, target);
        }
    }

    private static void ValidateSemanticTarget(
        string profileId,
        string key,
        SemanticTargetDefinition target)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(target.Intent))
        {
            throw new ProfileValidationException(
                $"Application profile '{profileId}' contains a semantic target without a key or intent.");
        }

        if (target.Strategies.Count == 0)
        {
            throw new ProfileValidationException(
                $"Semantic target '{key}' in profile '{profileId}' requires at least one selector strategy.");
        }

        if (target.Thresholds.MinimumConfidence is < 0 or > 1 ||
            target.Thresholds.MinimumMargin is < 0 or > 1)
        {
            throw new ProfileValidationException(
                $"Semantic target '{key}' in profile '{profileId}' has invalid resolution thresholds.");
        }

        foreach (var strategy in target.Strategies)
        {
            if (strategy.IsEmpty)
            {
                throw new ProfileValidationException(
                    $"Semantic target '{key}' in profile '{profileId}' contains an empty selector strategy.");
            }

            if (!string.IsNullOrWhiteSpace(strategy.SemanticKey))
            {
                throw new ProfileValidationException(
                    $"Semantic target '{key}' in profile '{profileId}' cannot reference another semantic key.");
            }

            if (strategy.Weight is < 0 or > 1)
            {
                throw new ProfileValidationException(
                    $"Semantic target '{key}' in profile '{profileId}' has a strategy weight outside 0 to 1.");
            }

            if (strategy.Index is < 0)
            {
                throw new ProfileValidationException(
                    $"Semantic target '{key}' in profile '{profileId}' has a negative strategy index.");
            }
        }

        foreach (var controlType in target.ExpectedControlTypes)
        {
            if (!Enum.TryParse<FlaUI.Core.Definitions.ControlType>(
                    controlType,
                    ignoreCase: true,
                    out _))
            {
                throw new ProfileValidationException(
                    $"Semantic target '{key}' in profile '{profileId}' has unknown control type '{controlType}'.");
            }
        }
    }
}
