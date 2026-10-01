namespace DesktopComputerUse.Contracts.Automation;

public sealed record RectangleInfo(double X, double Y, double Width, double Height)
{
    public string CoordinateSpace { get; init; } = "physicalVirtualScreen";
    public string Units { get; init; } = "pixels";
}
