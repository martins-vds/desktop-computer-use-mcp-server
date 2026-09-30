namespace DesktopComputerUse.Contracts.Profiles;

public sealed record SelectorScope
{
    public string? WindowKey { get; init; }

    public string? ViewKey { get; init; }

    public IReadOnlyList<string> AncestorLabels { get; init; } = [];
}
