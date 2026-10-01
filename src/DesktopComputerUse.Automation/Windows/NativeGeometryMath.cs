using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Windows;

public static class NativeGeometryMath
{
    public static PhysicalScreenPoint ClientToScreen(WindowClientPoint point, PhysicalScreenRect clientBounds) =>
        new(checked(clientBounds.X + point.X), checked(clientBounds.Y + point.Y));

    public static WindowClientPoint ScreenToClient(PhysicalScreenPoint point, PhysicalScreenRect clientBounds) =>
        new(checked(point.X - clientBounds.X), checked(point.Y - clientBounds.Y));

    public static int ScaleLogicalPixels(int pixels, uint dpi)
    {
        if (dpi == 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        return checked((int)Math.Round((double)pixels * dpi / 96, MidpointRounding.AwayFromZero));
    }

    public static (int X, int Y) ToAbsoluteMouse(PhysicalScreenPoint point, PhysicalScreenRect virtualScreen)
    {
        if (!virtualScreen.Contains(point) || virtualScreen.Width < 2 || virtualScreen.Height < 2 ||
            virtualScreen.Width > 65536 || virtualScreen.Height > 65536)
            throw new ArgumentOutOfRangeException(nameof(point));
        // Match the pixel bins used by absolute SendInput, rather than assuming origin (0, 0).
        return (
            AbsoluteAxis(point.X, virtualScreen.X, virtualScreen.Width),
            AbsoluteAxis(point.Y, virtualScreen.Y, virtualScreen.Height));
    }

    private static int AbsoluteAxis(int position, int origin, int length)
    {
        var offset = (long)position - origin;
        var center = (2 * offset + 1) * 65536 / (2L * length);
        var firstInBin = (offset * 65536 + length - 1) / length;
        return checked((int)Math.Max(center, firstInBin));
    }

    public static PhysicalScreenPoint CaptureToScreen(CaptureImagePoint point, CapturePixelTransform transform)
    {
        if (!transform.SourceRect.IsNonEmpty || transform.ImageWidth <= 0 || transform.ImageHeight <= 0 ||
            point.X < 0 || point.Y < 0 || point.X >= transform.ImageWidth || point.Y >= transform.ImageHeight)
            throw new ArgumentOutOfRangeException(nameof(point));
        return new(
            checked(transform.SourceRect.X + (int)((long)point.X * transform.SourceRect.Width / transform.ImageWidth)),
            checked(transform.SourceRect.Y + (int)((long)point.Y * transform.SourceRect.Height / transform.ImageHeight)));
    }

    public static bool CaptureIsCurrent(
        NativeCaptureToken token, NativeWindowTarget target, NativeCaptureGeneration generation,
        NativeWindowGeometry current) =>
        token.Target == target && token.Generation == generation && SameWindowGeometry(token.Geometry, current) &&
        !current.IsMinimized && current.IsVisible && !current.IsCloaked;

    public static bool SameWindowGeometry(NativeWindowGeometry expected, NativeWindowGeometry current) =>
        expected.Target == current.Target && expected.WindowBounds == current.WindowBounds &&
        expected.ClientBounds == current.ClientBounds && expected.ExtendedFrameBounds == current.ExtendedFrameBounds &&
        expected.Dpi == current.Dpi;

    public static bool IsUsableWindow(NativeWindowGeometry geometry) =>
        geometry.IsVisible && !geometry.IsMinimized && !geometry.IsCloaked;

    public static bool IsRestoredWindow(NativeWindowGeometry geometry, uint placementCommand) =>
        IsUsableWindow(geometry) && geometry.WindowBounds.IsNonEmpty && geometry.ClientBounds.IsNonEmpty &&
        placementCommand is not (2 or 6 or 7 or 11);

    public static bool IsOwnedWindow(bool isWindow, uint actualProcess, int targetProcess, bool isRoot) =>
        isWindow && actualProcess == targetProcess && isRoot;

    public static bool IsOwnedHit(uint actualProcess, int targetProcess, bool isTarget, bool isChild) =>
        actualProcess == targetProcess && (isTarget || isChild);

    public static (uint Down, uint Up) MouseButtonFlags(NativeMouseButton button) => button switch
    {
        NativeMouseButton.Left => (0x0002U, 0x0004U),
        NativeMouseButton.Right => (0x0008U, 0x0010U),
        NativeMouseButton.Middle => (0x0020U, 0x0040U),
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    public static PhysicalScreenRect MapSensitiveRegion(PhysicalScreenRect region, CapturePixelTransform transform)
    {
        var source = transform.SourceRect;
        if (!region.IsNonEmpty || !source.IsNonEmpty || transform.ImageWidth <= 0 || transform.ImageHeight <= 0 ||
            region.X < source.X || region.Y < source.Y || region.Right > source.Right || region.Bottom > source.Bottom)
            throw new ArgumentOutOfRangeException(nameof(region), "Sensitive bounds must be fully mapped inside the capture.");
        var left = (long)(region.X - (long)source.X) * transform.ImageWidth / source.Width;
        var top = (long)(region.Y - (long)source.Y) * transform.ImageHeight / source.Height;
        var right = ((region.Right - source.X) * transform.ImageWidth + source.Width - 1) / source.Width;
        var bottom = ((region.Bottom - source.Y) * transform.ImageHeight + source.Height - 1) / source.Height;
        return new(checked((int)left), checked((int)top), checked((int)(right - left)), checked((int)(bottom - top)));
    }
}
