using System.Runtime.InteropServices;
using DesktopComputerUse.Contracts.Automation;
using static DesktopComputerUse.Automation.Windows.Win32NativeMethods;

namespace DesktopComputerUse.Automation.Windows;

public sealed class Win32DesktopApi : IWindowsDesktopApi
{
    /// <summary>Call at process startup, before constructing automation workers or creating any HWND.</summary>
    public static void InitializePerMonitorV2()
    {
        RequireWindows();
        if (!SetProcessDpiAwarenessContext(PerMonitorV2) &&
            !AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), PerMonitorV2))
            throw Error(NativeFailureCode.DpiAwarenessRequired, "initializeDpi", "setAwareness", null, "Per-Monitor V2 DPI awareness could not be established.");
    }

    public NativeDesktopLayout GetDesktopLayout()
    {
        RequirePhysicalCoordinates();
        var collector = new MonitorCollector();
        if (!EnumDisplayMonitors(0, 0, collector.Collect, 0) || collector.Failed)
            throw Error(NativeFailureCode.InvalidWindow, "desktopLayout", "enumerateMonitors", null, "Monitor enumeration failed.");
        RequireMonitors(collector.Monitors);
        return new(new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)), collector.Monitors);
    }

    public NativeWindowGeometry GetGeometry(NativeWindowTarget target)
    {
        RequirePhysicalCoordinates();
        var hwnd = Validate(target);
        var window = ReadWindowBounds(hwnd, target);
        var client = ReadClientBounds(hwnd, target);
        var dpi = ReadWindowDpi(hwnd, target);
        PhysicalScreenRect? extended = DwmGetRect(hwnd, 9, out var frame, (uint)Marshal.SizeOf<Rect>()) == 0 ? ConvertRect(frame) : null;
        var cloaked = ReadCloaked(hwnd, target);
        Validate(target);
        return new(target, window, extended, client, dpi, IsWindowVisible(hwnd), IsIconic(hwnd), cloaked, GetForegroundWindow() == hwnd);
    }

    public bool QueueRestore(NativeWindowTarget target) => ShowWindowAsync(Validate(target), 9);

    public bool IsRestored(NativeWindowTarget target)
    {
        var hwnd = Validate(target);
        var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref placement)) return false;
        var geometry = GetGeometry(target);
        return NativeGeometryMath.IsRestoredWindow(geometry, placement.ShowCommand);
    }

    public bool TryActivate(NativeWindowTarget target)
    {
        var hwnd = Validate(target);
        if (IsForeground(target)) return true;
        // Do not bypass foreground-lock restrictions or change topmost state.
        BringWindowToTop(hwnd);
        SetForegroundWindow(Validate(target));
        return IsForeground(target);
    }

    public bool IsForeground(NativeWindowTarget target) => GetForegroundWindow() == Validate(target);

    public NativeWindowTarget? GetForegroundTarget()
    {
        RequireWindows();
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return null;
        GetWindowThreadProcessId(hwnd, out var process);
        return new(checked((int)process), hwnd.ToInt64());
    }

    public bool HasOwnedKeyboardFocus(NativeWindowTarget target)
    {
        var hwnd = Validate(target);
        return IsForeground(target) && IsTargetOrChild(target, ReadFocusedWindow(hwnd).ToInt64()) && IsForeground(target);
    }

    private static nint ReadFocusedWindow(nint hwnd)
    {
        var threadId = GetWindowThreadProcessId(hwnd, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(threadId, ref info) ? info.Focus : 0;
    }

    public long WindowAtPoint(PhysicalScreenPoint point)
    {
        RequirePhysicalCoordinates();
        return WindowFromPoint(new Point { X = point.X, Y = point.Y }).ToInt64();
    }

    public bool IsTargetOrChild(NativeWindowTarget target, long windowHandle)
    {
        var hwnd = Validate(target);
        var candidate = new nint(windowHandle);
        if (candidate == 0 || !IsWindow(candidate)) return false;
        GetWindowThreadProcessId(candidate, out var process);
        return NativeGeometryMath.IsOwnedHit(process, target.ProcessId, candidate == hwnd, IsChild(hwnd, candidate));
    }

    public uint SendMouse(PhysicalScreenPoint point, PhysicalScreenRect desktop, NativeMouseButton button)
    {
        RequirePhysicalCoordinates();
        var absolute = NativeGeometryMath.ToAbsoluteMouse(point, desktop);
        var (down, up) = NativeGeometryMath.MouseButtonFlags(button);
        Input[] inputs =
        [
            new() { Type = 0, Value = new() { Mouse = new() { X = absolute.X, Y = absolute.Y, Flags = MouseMove | MouseAbsolute | MouseVirtualDesk } } },
            new() { Type = 0, Value = new() { Mouse = new() { Flags = down } } },
            new() { Type = 0, Value = new() { Mouse = new() { Flags = up } } }
        ];
        var count = Dispatch(inputs);
        if (count != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            Dispatch([inputs[2]]); // Best-effort release after partial injection.
            throw InputFailure("click", error, count);
        }
        return count;
    }

    public PhysicalScreenPoint GetCursorPosition()
    {
        RequirePhysicalCoordinates();
        if (!GetCursorPos(out var point))
            throw Error(NativeFailureCode.InputDispatchFailed, "click", "getCursor", null, "Cursor position is unavailable.");
        return new(point.X, point.Y);
    }

    public uint SendUnicode(string text)
    {
        RequireWindows();
        var inputs = UnicodeInputs(text);
        var count = Dispatch(inputs);
        if (count != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            ReleasePartialUnicode(inputs, count);
            throw InputFailure("typeText", error, count);
        }
        return count;
    }

    public uint SendKeys(IReadOnlyList<ushort> keys)
    {
        RequireWindows();
        var inputs = keys.Select(key => Keyboard(key, flags: ExtendedFlag(key)))
            .Concat(keys.Reverse().Select(key => Keyboard(key, flags: ExtendedFlag(key) | KeyUp))).ToArray();
        var count = Dispatch(inputs);
        if (count != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            Dispatch(keys.Reverse().Select(key => Keyboard(key, flags: ExtendedFlag(key) | KeyUp)).ToArray());
            throw InputFailure("keyPress", error, count);
        }
        return count;
    }

    internal static nint Validate(NativeWindowTarget target)
    {
        RequireWindows();
        RequireIdentity(target);
        var hwnd = new nint(target.WindowHandle);
        GetWindowThreadProcessId(hwnd, out var process);
        if (!NativeGeometryMath.IsOwnedWindow(IsWindow(hwnd), process, target.ProcessId, GetAncestor(hwnd, 2) == hwnd))
            throw Error(NativeFailureCode.InvalidWindow, "validateWindow", "ownership", target, "HWND is not a top-level window of the attached process.");
        return hwnd;
    }

    private static void RequireIdentity(NativeWindowTarget target)
    {
        if (target.ProcessId <= 0 || target.WindowHandle == 0)
            throw Error(NativeFailureCode.InvalidWindow, "validateWindow", "identity", target, "Attached process/window identity is invalid.");
    }

    private static NativeMonitor? ReadMonitor(nint monitor)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), Device = "" };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        var dpi = ReadMonitorDpi(monitor);
        return new(monitor.ToInt64(), info.Device, ConvertRect(info.Monitor), ConvertRect(info.Work), (info.Flags & 1) != 0, dpi.X, dpi.Y);
    }
    private static void RequireMonitors(List<NativeMonitor> monitors)
    {
        if (monitors.Count == 0)
            throw Error(NativeFailureCode.InvalidWindow, "desktopLayout", "enumerateMonitors", null, "No monitors are available.");
    }
    private sealed class MonitorCollector
    {
        public List<NativeMonitor> Monitors { get; } = [];
        public bool Failed { get; private set; }
        public bool Collect(nint monitor, nint dc, ref Rect bounds, nint data)
        {
            var info = ReadMonitor(monitor);
            if (info is null) { Failed = true; return false; }
            Monitors.Add(info);
            return true;
        }
    }
    private static (uint? X, uint? Y) ReadMonitorDpi(nint monitor) =>
        GetDpiForMonitor(monitor, 0, out var x, out var y) == 0 ? (x, y) : (null, null);
    private static PhysicalScreenRect ReadWindowBounds(nint hwnd, NativeWindowTarget target)
    {
        if (!GetWindowRect(hwnd, out var rect))
            throw Error(NativeFailureCode.InvalidWindow, "geometry", "getBounds", target, "Native window bounds are unavailable.");
        return ConvertRect(rect);
    }
    private static PhysicalScreenRect ReadClientBounds(nint hwnd, NativeWindowTarget target)
    {
        if (!GetClientRect(hwnd, out var rect))
            throw Error(NativeFailureCode.InvalidWindow, "geometry", "getClientBounds", target, "Native client bounds are unavailable.");
        var origin = ClientOrigin(hwnd, rect, target);
        return new(origin.X, origin.Y, checked(rect.Right - rect.Left), checked(rect.Bottom - rect.Top));
    }
    private static Point ClientOrigin(nint hwnd, Rect rect, NativeWindowTarget target)
    {
        var origin = new Point { X = rect.Left, Y = rect.Top };
        if (!ClientToScreen(hwnd, ref origin))
            throw Error(NativeFailureCode.InvalidWindow, "geometry", "clientToScreen", target, "Client bounds could not be mapped.");
        return origin;
    }
    private static uint ReadWindowDpi(nint hwnd, NativeWindowTarget target)
    {
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) throw Error(NativeFailureCode.InvalidWindow, "geometry", "getDpi", target, "Window DPI is unavailable.");
        return dpi;
    }
    private static bool ReadCloaked(nint hwnd, NativeWindowTarget target)
    {
        if (DwmGetUInt(hwnd, 14, out var cloaked, sizeof(uint)) != 0)
            throw Error(NativeFailureCode.InvalidWindow, "geometry", "getCloaked", target, "Window cloak state is unavailable.");
        return cloaked != 0;
    }
    private static Input[] UnicodeInputs(string text)
    {
        var inputs = new Input[checked(text.Length * 2)];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = Keyboard(scan: text[i], flags: KeyUnicode);
            inputs[i * 2 + 1] = Keyboard(scan: text[i], flags: KeyUnicode | KeyUp);
        }
        return inputs;
    }
    private static void ReleasePartialUnicode(Input[] inputs, uint count)
    {
        if (count > 0 && count % 2 == 1) Dispatch([inputs[count]]);
    }

    internal static void RequirePhysicalCoordinates()
    {
        RequireWindows();
        if (!AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), PerMonitorV2))
            throw Error(NativeFailureCode.DpiAwarenessRequired, "geometry", "verifyAwareness", null, "Per-Monitor V2 DPI awareness must be initialized before native operations.");
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new NativeOperationException(NativeFailureCode.PlatformNotSupported, "nativeDesktop", "platform", "Native desktop operations require Windows.");
    }

    private static uint ExtendedFlag(ushort key) => key is >= 0x21 and <= 0x2E ? KeyExtended : 0;
    private static Input Keyboard(ushort key = 0, ushort scan = 0, uint flags = 0) =>
        new() { Type = 1, Value = new() { Keyboard = new() { VirtualKey = key, Scan = scan, Flags = flags } } };
    private static uint Dispatch(Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    private static NativeOperationException InputFailure(string operation, int error, uint dispatched) =>
        new(NativeFailureCode.InputDispatchFailed, operation, "sendInput",
            "SendInput did not completely dispatch; partial input may have occurred. UIPI can block input without setting a Win32 error.",
            win32Error: error) { DispatchedInputCount = dispatched };
    private static PhysicalScreenRect ConvertRect(Rect rect) =>
        new(rect.Left, rect.Top, checked(rect.Right - rect.Left), checked(rect.Bottom - rect.Top));
    private static NativeOperationException Error(NativeFailureCode code, string operation, string phase,
        NativeWindowTarget? target, string message) => new(code, operation, phase, message, target, Marshal.GetLastWin32Error());
}
