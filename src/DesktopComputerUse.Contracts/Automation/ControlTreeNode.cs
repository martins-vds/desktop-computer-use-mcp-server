namespace DesktopComputerUse.Contracts.Automation;

public sealed record ControlTreeNode(
    ControlSummary Control,
    IReadOnlyList<ControlTreeNode> Children);
