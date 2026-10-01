using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Windows;

public interface IWindowCaptureProvider
{
    Task<NativeWindowCapture> CaptureAsync(NativeWindowTarget target, NativeCaptureOptions options,
        CancellationToken cancellationToken = default);
    NativeCaptureToken GetCaptureToken(string captureId, NativeWindowTarget target, NativeCaptureGeneration generation);
    void InvalidateSession(string sessionId);
}

/// <summary>Top-down BGRA pixels in physical source-rectangle space. Only attached-window pixels may be returned.</summary>
public sealed record NativePixelBuffer(int Width, int Height, byte[] BgraPixels);

public interface INativeWindowPixelSource
{
    NativePixelBuffer? TryPrintWindow(NativeWindowTarget target, PhysicalScreenRect source);
}
