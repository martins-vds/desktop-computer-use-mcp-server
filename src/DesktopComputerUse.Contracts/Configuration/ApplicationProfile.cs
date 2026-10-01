using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Profiles;
using System.Text.Json.Serialization;

namespace DesktopComputerUse.Contracts.Configuration;

public sealed record ApplicationProfile
{
    public int SchemaVersion { get; init; } = 1;

    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public AutomationBackend Backend { get; init; } = AutomationBackend.Uia3;

    public WindowSelector MainWindow { get; init; } = new();

    public int OperationTimeoutMs { get; init; } = 10_000;

    public int PollIntervalMs { get; init; } = 100;

    public int MaxTreeDepth { get; init; } = 5;

    public int MaxResults { get; init; } = 200;

    public bool EnableScreenshots { get; init; }

    public bool AllowMultipleInstances { get; init; }

    public void ValidateWindowAndBackend()
    {
        if (!Enum.IsDefined(Backend))
        {
            throw new ProfileValidationException("The application profile has an unsupported automation backend.");
        }
        if (MainWindow is null)
        {
            throw new ProfileValidationException("The application profile requires a main-window selector.");
        }
    }

    public NativeInputPolicy NativeInput { get; init; } = new();

    [JsonIgnore]
    public ApplicationProfileMetadata? Metadata { get; init; }

    public IReadOnlyDictionary<string, ControlSelector> SemanticSelectors { get; init; }
        = new Dictionary<string, ControlSelector>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, SemanticTargetDefinition> SemanticTargets { get; init; }
        = new Dictionary<string, SemanticTargetDefinition>(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> SensitiveAutomationIds { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public IReadOnlyDictionary<string, SemanticTargetDefinition> EffectiveSemanticTargets
    {
        get
        {
            var targets = SemanticSelectors.ToDictionary(
                pair => pair.Key,
                pair => SemanticTargetDefinition.FromLegacy(pair.Key, pair.Value),
                StringComparer.OrdinalIgnoreCase);
            foreach (var (key, target) in SemanticTargets)
            {
                targets[key] = target;
            }

            return targets;
        }
    }
}
