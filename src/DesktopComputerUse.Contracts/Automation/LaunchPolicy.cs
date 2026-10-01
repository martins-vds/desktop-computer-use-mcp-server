namespace DesktopComputerUse.Contracts.Automation;

public enum LaunchPolicy
{
    Fail,
    Attach,
    LaunchNew
}

public sealed record MatchingApplicationProcess(
    int ProcessId,
    string ExecutablePath,
    string? MainWindowTitle);
