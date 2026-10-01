namespace DesktopComputerUse.Contracts.Automation;

public enum NativeMouseButton { Left, Right, Middle }
public enum NativeInputBounds { ClientArea, Window }

/// <summary>Explicit per-operation authorization. The caller must derive this from the attached profile.</summary>
public sealed record NativeOperationPolicy
{
    public bool Enabled { get; init; }
    public bool AllowMouse { get; init; } = true;
    public bool AllowKeyboard { get; init; }
    public IReadOnlyList<NativeMouseButton> AllowedMouseButtons { get; init; } = [NativeMouseButton.Left];
    public NativeInputBounds ConstrainTo { get; init; } = NativeInputBounds.ClientArea;
    public bool RequireForeground { get; init; } = true;
    public int MaximumTextLength { get; init; } = 500;
    public bool AllowSystemKeys { get; init; }
    public bool AllowRestore { get; init; }
    public bool AllowActivate { get; init; }
}

public sealed record NativeClickResult(
    PhysicalScreenPoint RequestedPoint, PhysicalScreenPoint ActualPoint,
    long HitWindowHandle, bool IsForeground, uint DispatchedInputCount);

public sealed record NativeKeyboardResult(long WindowHandle, bool IsForeground, uint DispatchedInputCount);

/// <summary>Broker-local failure codes; callers map these to their public automation error contracts.</summary>
public enum NativeFailureCode
{
    PlatformNotSupported, DpiAwarenessRequired, InvalidWindow, RawInputDisabled,
    InvalidArgument, PointOutsideTarget, ForeignWindowAtPoint, ForegroundRequired,
    ForeignKeyboardFocus, WindowRestoreFailed, WindowActivationFailed, InputDispatchFailed,
    CaptureDisabled, CaptureFailed, UnsafeScreenCapture, SensitiveGeometryUnavailable, StaleCapture
}

public sealed class NativeOperationException : Exception
{
    public NativeOperationException(
        NativeFailureCode code, string operation, string phase, string message,
        NativeWindowTarget? target = null, int? win32Error = null) : base(message)
    {
        Code = code;
        Operation = operation;
        Phase = phase;
        Target = target;
        Win32Error = win32Error;
    }

    public NativeFailureCode Code { get; }
    public string Operation { get; }
    public string Phase { get; }
    public NativeWindowTarget? Target { get; }
    public NativeWindowTarget? ForegroundTarget { get; set; }
    public int? Win32Error { get; }
    public uint? DispatchedInputCount { get; init; }
}
