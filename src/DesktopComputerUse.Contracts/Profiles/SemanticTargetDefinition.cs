using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Contracts.Profiles;

public sealed record SemanticTargetDefinition
{
    public required string Intent { get; init; }

    public IReadOnlyList<string> Synonyms { get; init; } = [];

    public IReadOnlyList<string> ExpectedControlTypes { get; init; } = [];

    public IReadOnlyList<string> RequiredPatterns { get; init; } = [];

    public SelectorScope Scope { get; init; } = new();

    public IReadOnlyList<SelectorStrategy> Strategies { get; init; } = [];

    public ResolutionThresholds Thresholds { get; init; } = new();

    public ControlFingerprint? Fingerprint { get; init; }

    public bool AllowAiAssistance { get; init; }

    public static SemanticTargetDefinition FromLegacy(
        string key,
        ControlSelector selector)
        => new()
        {
            Intent = key.Replace('-', ' '),
            ExpectedControlTypes = string.IsNullOrWhiteSpace(selector.ControlType)
                ? []
                : [selector.ControlType],
            Strategies =
            [
                new SelectorStrategy
                {
                    AutomationId = selector.AutomationId,
                    Name = selector.Name,
                    ControlType = selector.ControlType,
                    ClassName = selector.ClassName,
                    Ancestor = selector.Ancestor,
                    Index = selector.Index,
                    Weight = 1
                }
            ],
            Thresholds = new ResolutionThresholds
            {
                MinimumConfidence = 1,
                MinimumMargin = 1
            }
        };
}
