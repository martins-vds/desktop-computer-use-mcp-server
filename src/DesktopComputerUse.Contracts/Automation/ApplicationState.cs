namespace DesktopComputerUse.Contracts.Automation;

public sealed record ApplicationState(
    string ProfileId,
    int ProcessId,
    string ExecutablePath,
    bool OwnsProcess,
    bool HasExited,
    string? ActiveWindowTitle,
    AutomationBackend Backend)
{
    public bool PrivacyMode { get; init; } = true;

    public string? ProfileRevision { get; init; }
    public long ProfileGeneration { get; init; }
    public bool ProfileIsStale { get; init; }
    public string? SessionId { get; init; }
}
