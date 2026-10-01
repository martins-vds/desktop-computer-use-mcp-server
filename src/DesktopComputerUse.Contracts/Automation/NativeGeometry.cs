namespace DesktopComputerUse.Contracts.Automation;

// All native screen coordinates are physical pixels, including negative virtual-screen origins.
public readonly record struct PhysicalScreenPoint(int X, int Y);
public readonly record struct WindowClientPoint(int X, int Y);
public readonly record struct CaptureImagePoint(int X, int Y);

public readonly record struct PhysicalScreenRect(int X, int Y, int Width, int Height)
{
    public long Right => (long)X + Width;
    public long Bottom => (long)Y + Height;
    public bool IsNonEmpty => Width > 0 && Height > 0;
    public bool Contains(PhysicalScreenPoint point) =>
        IsNonEmpty && point.X >= X && point.Y >= Y && point.X < Right && point.Y < Bottom;
}

public readonly record struct WindowClientRect(int X, int Y, int Width, int Height);

public sealed record NativeWindowTarget(int ProcessId, long WindowHandle);
public sealed record NativeMonitor(
    long MonitorHandle, string DeviceName, PhysicalScreenRect Bounds,
    PhysicalScreenRect WorkArea, bool IsPrimary, uint? DpiX = null, uint? DpiY = null);
public sealed record NativeDesktopLayout(PhysicalScreenRect VirtualScreen, IReadOnlyList<NativeMonitor> Monitors)
{
    public string CoordinateSpace => "physicalVirtualScreen";
    public string Units => "pixels";
}

public sealed record NativeWindowGeometry(
    NativeWindowTarget Target,
    PhysicalScreenRect WindowBounds,
    PhysicalScreenRect? ExtendedFrameBounds,
    PhysicalScreenRect ClientBounds,
    uint Dpi,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsForeground)
{
    public string CoordinateSpace => "physicalVirtualScreen";
    public string Units => "pixels";
    public RectangleInfo? UiaBounds { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record NativeLifecycleResult(NativeWindowGeometry Geometry, bool Restored, bool Activated);
