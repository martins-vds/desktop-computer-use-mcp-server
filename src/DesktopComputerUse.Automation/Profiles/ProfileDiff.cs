using System.Text.Json;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Profiles;

public static class ProfileDiff
{
    public static IReadOnlyList<string> Compare(
        ApplicationProfile original,
        ApplicationProfile draft,
        JsonSerializerOptions options)
    {
        var differences = CompareTargets(original, draft, options).ToList();
        if (!MetadataEquals(original, draft, options))
        {
            differences.Add("~ profile metadata");
        }

        return differences.Count == 0
            ? ["No semantic profile differences."]
            : differences;
    }

    private static IEnumerable<string> CompareTargets(
        ApplicationProfile original,
        ApplicationProfile draft,
        JsonSerializerOptions options)
    {
        foreach (var key in original.EffectiveSemanticTargets.Keys
                     .Union(draft.EffectiveSemanticTargets.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            var difference = CompareTarget(original, draft, key, options);
            if (difference is not null)
            {
                yield return difference;
            }
        }
    }

    private static string? CompareTarget(
        ApplicationProfile original,
        ApplicationProfile draft,
        string key,
        JsonSerializerOptions options)
    {
        var hasOriginal = original.EffectiveSemanticTargets.TryGetValue(
            key,
            out var originalTarget);
        var hasDraft = draft.EffectiveSemanticTargets.TryGetValue(
            key,
            out var draftTarget);
        if (!hasOriginal)
        {
            return $"+ semanticTargets.{key}";
        }

        if (!hasDraft)
        {
            return $"- semanticTargets.{key}";
        }

        return JsonSerializer.Serialize(originalTarget, options) ==
            JsonSerializer.Serialize(draftTarget, options)
            ? null
            : $"~ semanticTargets.{key}";
    }

    private static bool MetadataEquals(
        ApplicationProfile original,
        ApplicationProfile draft,
        JsonSerializerOptions options)
        => JsonSerializer.Serialize(WithoutTargets(original), options) ==
            JsonSerializer.Serialize(WithoutTargets(draft), options);

    private static ApplicationProfile WithoutTargets(ApplicationProfile profile)
        => profile with
        {
            SemanticSelectors = new Dictionary<string, ControlSelector>(),
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>()
        };
}
