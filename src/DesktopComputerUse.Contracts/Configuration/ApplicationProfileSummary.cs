using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Contracts.Configuration;

public sealed record ApplicationProfileSummary(
    string Id,
    string DisplayName,
    string ExecutablePath,
    AutomationBackend Backend,
    bool EnableScreenshots);
