using System.Runtime.InteropServices;
using DesktopComputerUse.Contracts.Automation;
using static DesktopComputerUse.Automation.Windows.Win32NativeMethods;

namespace DesktopComputerUse.Automation.Windows;

internal sealed class GdiCaptureSurface : IDisposable
{
    private nint screenDc, memoryDc, bitmap, previous, bits;
    private readonly PhysicalScreenRect source;
    private GdiCaptureSurface(PhysicalScreenRect source) => this.source = source;
    public nint DeviceContext => memoryDc;

    public static GdiCaptureSurface Create(PhysicalScreenRect source)
    {
        var surface = new GdiCaptureSurface(source);
        try
        {
            surface.CreateContexts();
            surface.CreateBitmap();
            surface.SelectBitmap();
            surface.ClearPixels();
            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    private void CreateContexts()
    {
        screenDc = RequireHandle(GetDC(0));
        memoryDc = RequireHandle(CreateCompatibleDC(screenDc));
    }
    private void CreateBitmap()
    {
        var info = new BitmapInfo
        {
            Size = 40, Width = source.Width, Height = -source.Height, Planes = 1, BitCount = 32, Compression = 0
        };
        bitmap = RequireHandle(CreateDIBSection(screenDc, ref info, 0, out bits, 0, 0));
        RequireHandle(bits);
    }
    private void SelectBitmap()
    {
        previous = SelectObject(memoryDc, bitmap);
        if (previous == 0 || previous == new nint(-1)) throw AllocationFailed();
    }
    private static nint RequireHandle(nint handle)
    {
        if (handle == 0) throw AllocationFailed();
        return handle;
    }
    private void ClearPixels()
    {
        // A provider that renders incompletely must not expose recycled GDI memory.
        var buffer = new byte[checked(source.Width * source.Height * 4)];
        Marshal.Copy(buffer, 0, bits, buffer.Length);
    }
    public NativePixelBuffer ReadPixels()
    {
        var buffer = new byte[checked(source.Width * source.Height * 4)];
        Marshal.Copy(bits, buffer, 0, buffer.Length);
        return new(source.Width, source.Height, buffer);
    }
    public void Dispose()
    {
        RestoreSelection();
        DeleteBitmap();
        DeleteMemoryContext();
        ReleaseScreenContext();
    }
    private void RestoreSelection()
    {
        if (previous != 0 && previous != new nint(-1)) SelectObject(memoryDc, previous);
        previous = 0;
    }
    private void DeleteBitmap()
    {
        if (bitmap != 0) DeleteObject(bitmap);
        bitmap = 0;
    }
    private void DeleteMemoryContext()
    {
        if (memoryDc != 0) DeleteDC(memoryDc);
        memoryDc = 0;
    }
    private void ReleaseScreenContext()
    {
        if (screenDc != 0) ReleaseDC(0, screenDc);
        screenDc = 0;
    }
    private static NativeOperationException AllocationFailed() =>
        new(NativeFailureCode.CaptureFailed, "capture", "allocate", "Native capture surface allocation failed.",
            win32Error: Marshal.GetLastWin32Error());
}
