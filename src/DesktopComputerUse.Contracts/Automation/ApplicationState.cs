namespace DesktopComputerUse.Contracts.Automation;

public sealed record ApplicationState(
    string ProfileId,
    int ProcessId,
    string ExecutablePath,
    bool OwnsProcess,
    bool HasExited,
    string? ActiveWindowTitle,
    AutomationBackend Backend);
