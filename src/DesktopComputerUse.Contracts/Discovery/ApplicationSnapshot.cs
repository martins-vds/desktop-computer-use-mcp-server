using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Contracts.Discovery;

public sealed record ApplicationSnapshot(
    string ProfileId,
    int ProcessId,
    string ExecutablePath,
    AutomationBackend Backend,
    DateTimeOffset CapturedAt,
    WindowSnapshot Window);
