using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Contracts.Profiles;

public sealed record SelectorStrategy : ControlSelector
{
    public double Weight { get; init; } = 1;
}
