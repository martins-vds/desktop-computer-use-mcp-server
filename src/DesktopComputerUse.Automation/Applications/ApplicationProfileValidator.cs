using DesktopComputerUse.Contracts.Configuration;

namespace DesktopComputerUse.Automation.Applications;

public static class ApplicationProfileValidator
{
    public static void Validate(ApplicationProfile profile)
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
}
