using DesktopComputerUse.Contracts.Automation;
using static DesktopComputerUse.Automation.Windows.Win32NativeMethods;

namespace DesktopComputerUse.Automation.Windows;

public sealed class PrintWindowPixelSource : INativeWindowPixelSource
{
    public NativePixelBuffer? TryPrintWindow(NativeWindowTarget target, PhysicalScreenRect source)
    {
        Win32DesktopApi.RequirePhysicalCoordinates();
        var hwnd = Win32DesktopApi.Validate(target);
        ValidateSize(target, source);
        using var surface = GdiCaptureSurface.Create(source);
        if (!PrintWindow(hwnd, surface.DeviceContext, PwRenderFullContent)) return null;
        Win32DesktopApi.Validate(target);
        return surface.ReadPixels();
    }

    private static void ValidateSize(NativeWindowTarget target, PhysicalScreenRect source)
    {
        if (!source.IsNonEmpty || (long)source.Width * source.Height > CaptureImageEncoder.MaximumPixels)
            throw new NativeOperationException(NativeFailureCode.CaptureFailed, "capture", "allocate", "Capture dimensions exceed the supported limit.", target);
    }
}
