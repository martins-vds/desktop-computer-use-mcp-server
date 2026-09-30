using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Contracts.Discovery;

public sealed record WindowSnapshot(
    string CandidateId,
    string? Title,
    string? AutomationId,
    string? ClassName,
    RectangleInfo Bounds,
    ViewSignature View,
    IReadOnlyList<ControlSnapshot> Controls,
    bool IsComplete);
