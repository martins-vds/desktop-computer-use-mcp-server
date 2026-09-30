namespace DesktopComputerUse.Contracts.Profiles;

public sealed record ControlFingerprint
{
    public string? ControlType { get; init; }

    public IReadOnlyList<string> SupportedPatterns { get; init; } = [];

    public string? ClassName { get; init; }

    public string? FrameworkId { get; init; }

    public IReadOnlyList<string> NameTokens { get; init; } = [];

    public IReadOnlyList<string> NearbyLabelTokens { get; init; } = [];

    public IReadOnlyList<string> AncestorTokens { get; init; } = [];

    public string? RelativeRegion { get; init; }

    public double? RelativeWidth { get; init; }

    public double? RelativeHeight { get; init; }

    public string? ApplicationVersion { get; init; }
}
