namespace DesktopComputerUse.Contracts.Configuration;

public sealed record WindowSelector
{
    public string? Title { get; init; }

    public string? TitleRegex { get; init; }

    public string? ClassName { get; init; }
}
