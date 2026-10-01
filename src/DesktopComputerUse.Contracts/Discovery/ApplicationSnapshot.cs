using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Contracts.Discovery;

public sealed record ApplicationSnapshot(
    string ProfileId,
    int ProcessId,
    string ExecutablePath,
    AutomationBackend Backend,
    DateTimeOffset CapturedAt,
    WindowSnapshot Window)
{
    public bool Partial => Window.Partial;
    public bool Truncated => Window.Truncated;
    public IReadOnlyList<AutomationDiagnostic> Failures => Window.Failures;
}
