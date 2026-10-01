using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Windows;

/// <summary>Test seam for native calls. Implementations must validate HWND/process identity on every geometry read.</summary>
public interface IWindowsDesktopApi
{
    NativeDesktopLayout GetDesktopLayout();
    NativeWindowGeometry GetGeometry(NativeWindowTarget target);
    bool QueueRestore(NativeWindowTarget target);
    bool TryActivate(NativeWindowTarget target);
    bool IsRestored(NativeWindowTarget target);
    bool IsForeground(NativeWindowTarget target);
    NativeWindowTarget? GetForegroundTarget();
    bool HasOwnedKeyboardFocus(NativeWindowTarget target);
    long WindowAtPoint(PhysicalScreenPoint point);
    bool IsTargetOrChild(NativeWindowTarget target, long windowHandle);
    uint SendMouse(PhysicalScreenPoint point, PhysicalScreenRect desktop, NativeMouseButton button);
    PhysicalScreenPoint GetCursorPosition();
    uint SendUnicode(string text);
    uint SendKeys(IReadOnlyList<ushort> keys);
}
