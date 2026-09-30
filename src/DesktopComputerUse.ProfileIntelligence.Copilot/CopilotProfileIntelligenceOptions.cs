namespace DesktopComputerUse.ProfileIntelligence.Copilot;

public sealed record CopilotProfileIntelligenceOptions
{
    public string Model { get; init; } = "auto";

    public string BaseDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopComputerUse",
        "CopilotProfileSessions");

    public bool UseLoggedInUser { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    public int MaximumCandidates { get; init; } = 15;
}
