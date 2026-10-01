using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Windows;

public sealed class WindowsDesktopBroker
{
    private readonly IWindowsDesktopApi api;
    private readonly TimeSpan lifecycleTimeout;
    internal SemaphoreSlim OperationGate { get; } = new(1, 1);
    internal IWindowsDesktopApi Api => api;

    public WindowsDesktopBroker(IWindowsDesktopApi? api = null, TimeSpan? lifecycleTimeout = null)
    {
        this.api = api ?? new Win32DesktopApi();
        this.lifecycleTimeout = lifecycleTimeout ?? TimeSpan.FromSeconds(2);
        if (this.lifecycleTimeout <= TimeSpan.Zero || this.lifecycleTimeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(lifecycleTimeout));
    }

    public NativeDesktopLayout GetDesktopLayout() => api.GetDesktopLayout();
    public NativeWindowGeometry GetWindowGeometry(NativeWindowTarget target) => api.GetGeometry(target);

    public Task<NativeLifecycleResult> RestoreWindowAsync(NativeWindowTarget target, CancellationToken cancellationToken = default) =>
        SerializedAsync(async () =>
        {
            var restored = await RestoreCoreAsync(target, cancellationToken);
            return new NativeLifecycleResult(api.GetGeometry(target), restored, false);
        }, cancellationToken);

    public Task<NativeLifecycleResult> ActivateWindowAsync(NativeWindowTarget target, bool allowRestore = false,
        CancellationToken cancellationToken = default) => SerializedAsync(async () =>
        {
            var restored = false;
            if (api.GetGeometry(target).IsMinimized && allowRestore)
                restored = await RestoreCoreAsync(target, cancellationToken);
            var activated = await ActivateCoreAsync(target, cancellationToken);
            return new NativeLifecycleResult(api.GetGeometry(target), restored, activated);
        }, cancellationToken);

    public Task<NativeClickResult> ClickScreenPointAsync(NativeWindowTarget target, PhysicalScreenPoint point,
        NativeOperationPolicy policy, NativeMouseButton button = NativeMouseButton.Left,
        CancellationToken cancellationToken = default) =>
        ClickAsync(target, policy, button, _ => point, cancellationToken);

    public Task<NativeClickResult> ClickClientPointAsync(NativeWindowTarget target, WindowClientPoint point,
        NativeOperationPolicy policy, NativeMouseButton button = NativeMouseButton.Left,
        CancellationToken cancellationToken = default) =>
        ClickAsync(target, policy, button, geometry => NativeGeometryMath.ClientToScreen(point, geometry.ClientBounds), cancellationToken);

    public Task<NativeClickResult> ClickCapturePointAsync(NativeWindowTarget target, NativeCaptureToken capture,
        NativeCaptureGeneration currentGeneration, CaptureImagePoint point, NativeOperationPolicy policy,
        NativeMouseButton button = NativeMouseButton.Left, CancellationToken cancellationToken = default) =>
        ClickAsync(target, policy, button, geometry =>
        {
            if (!NativeGeometryMath.CaptureIsCurrent(capture, target, currentGeneration, geometry))
                throw Failure(NativeFailureCode.StaleCapture, "clickCapturePoint", "validateCapture", target, "Capture no longer matches this session/window geometry.");
            return NativeGeometryMath.CaptureToScreen(point, capture.Transform);
        }, cancellationToken);

    public Task<NativeKeyboardResult> TypeTextAsync(NativeWindowTarget target, string text, NativeOperationPolicy policy,
        CancellationToken cancellationToken = default) => SerializedAsync(async () =>
        {
            ValidatePolicy(policy, keyboard: true);
            if (text is null || text.Length == 0 || text.Length > policy.MaximumTextLength ||
                text.Any(char.IsControl) || !IsWellFormedUtf16(text))
                throw Failure(NativeFailureCode.InvalidArgument, "typeText", "validateText", target, "Text must be bounded, well-formed Unicode without control characters.");
            await PrepareAsync(target, policy, cancellationToken);
            VerifyKeyboard(target);
            var count = api.SendUnicode(text);
            if (count != checked((uint)text.Length * 2))
                throw Failure(NativeFailureCode.InputDispatchFailed, "typeText", "sendInput", target, "Input was not completely dispatched; partial input may have occurred.", count);
            VerifyKeyboardAfterDispatch(target, count);
            return new NativeKeyboardResult(target.WindowHandle, true, count);
        }, cancellationToken);

    public Task<NativeKeyboardResult> KeyPressAsync(NativeWindowTarget target, string chord, NativeOperationPolicy policy,
        CancellationToken cancellationToken = default) => SerializedAsync(async () =>
        {
            ValidatePolicy(policy, keyboard: true);
            var keys = NativeKeyChords.Parse(chord, policy.AllowSystemKeys);
            await PrepareAsync(target, policy, cancellationToken);
            VerifyKeyboard(target);
            var count = api.SendKeys(keys);
            if (count != keys.Count * 2)
                throw Failure(NativeFailureCode.InputDispatchFailed, "keyPress", "sendInput", target, "Input was not completely dispatched; partial input may have occurred.", count);
            VerifyKeyboardAfterDispatch(target, count);
            return new NativeKeyboardResult(target.WindowHandle, true, count);
        }, cancellationToken);

    private Task<NativeClickResult> ClickAsync(NativeWindowTarget target, NativeOperationPolicy policy,
        NativeMouseButton button, Func<NativeWindowGeometry, PhysicalScreenPoint> convert, CancellationToken cancellationToken) =>
        SerializedAsync(async () =>
        {
            ValidatePolicy(policy, keyboard: false);
            RequireMouseButton(target, policy, button);
            await PrepareAsync(target, policy, cancellationToken);
            return DispatchClick(target, policy, button, convert, cancellationToken);
        }, cancellationToken);

    private NativeClickResult DispatchClick(NativeWindowTarget target, NativeOperationPolicy policy,
        NativeMouseButton button, Func<NativeWindowGeometry, PhysicalScreenPoint> convert, CancellationToken cancellationToken)
    {
        // Conversion and confinement must use geometry AFTER restore/activation, not stale cached bounds.
        var geometry = api.GetGeometry(target);
        RequireClickState(geometry);
        var point = ConvertPoint(target, geometry, convert);
        var desktop = api.GetDesktopLayout().VirtualScreen;
        RequireConfinedPoint(target, point, geometry, desktop, policy.ConstrainTo);
        RequireStableGeometry(target, geometry);
        RequireMouseForeground(target, policy.RequireForeground);
        var hit = RequireOwnedHit(target, point);
        cancellationToken.ThrowIfCancellationRequested();
        var count = api.SendMouse(point, desktop, button);
        if (count != 3)
            throw Failure(NativeFailureCode.InputDispatchFailed, "click", "sendInput", target, "Mouse input was not completely dispatched; partial input may have occurred.", count);
        return new(point, api.GetCursorPosition(), hit, api.IsForeground(target), count);
    }

    private static void RequireMouseButton(NativeWindowTarget target, NativeOperationPolicy policy, NativeMouseButton button)
    {
        if (!Enum.IsDefined(button) || !policy.AllowedMouseButtons.Contains(button))
            throw Failure(NativeFailureCode.RawInputDisabled, "click", "validateButton", target, "Mouse button is not allowed.");
    }
    private static void RequireClickState(NativeWindowGeometry geometry)
    {
        if (!NativeGeometryMath.IsUsableWindow(geometry))
            throw Failure(NativeFailureCode.InvalidWindow, "click", "validateState", geometry.Target, "Target is not visible and usable.");
    }
    private static PhysicalScreenPoint ConvertPoint(NativeWindowTarget target, NativeWindowGeometry geometry,
        Func<NativeWindowGeometry, PhysicalScreenPoint> convert)
    {
        try { return convert(geometry); }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            throw Failure(NativeFailureCode.InvalidArgument, "click", "convertPoint", target, "Point cannot be mapped to valid physical screen coordinates.");
        }
    }
    private static void RequireConfinedPoint(NativeWindowTarget target, PhysicalScreenPoint point,
        NativeWindowGeometry geometry, PhysicalScreenRect desktop, NativeInputBounds bounds)
    {
        var area = bounds == NativeInputBounds.ClientArea ? geometry.ClientBounds : geometry.WindowBounds;
        if (!area.Contains(point))
            throw Failure(NativeFailureCode.PointOutsideTarget, "click", "validatePoint", target, "Point is outside the allowed target area.");
        if (!desktop.Contains(point))
            throw Failure(NativeFailureCode.PointOutsideTarget, "click", "validatePoint", target, "Point is outside the virtual desktop.");
    }
    private void RequireStableGeometry(NativeWindowTarget target, NativeWindowGeometry geometry)
    {
        if (!NativeGeometryMath.SameWindowGeometry(geometry, api.GetGeometry(target)))
            throw Failure(NativeFailureCode.StaleCapture, "click", "validateGeometry", target, "Window moved during input validation.");
    }
    private void RequireMouseForeground(NativeWindowTarget target, bool requireForeground)
    {
        if (requireForeground && !api.IsForeground(target))
            throw Failure(NativeFailureCode.ForegroundRequired, "click", "verifyForeground", target, "Target is not foreground.");
    }
    private long RequireOwnedHit(NativeWindowTarget target, PhysicalScreenPoint point)
    {
        var hit = api.WindowAtPoint(point);
        if (!api.IsTargetOrChild(target, hit))
            throw Failure(NativeFailureCode.ForeignWindowAtPoint, "click", "hitTest", target, "Point is occluded by a foreign window.");
        return hit;
    }

    private async Task PrepareAsync(NativeWindowTarget target, NativeOperationPolicy policy, CancellationToken cancellationToken)
    {
        var geometry = api.GetGeometry(target);
        if (ShouldRestore(geometry, policy)) await RestoreCoreAsync(target, cancellationToken);
        if (ShouldActivate(target, policy))
            await ActivateCoreAsync(target, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<bool> RestoreCoreAsync(NativeWindowTarget target, CancellationToken cancellationToken)
    {
        api.GetGeometry(target);
        if (api.IsRestored(target)) return false;
        QueueRestore(target);
        if (!await PollAsync(() => api.IsRestored(target), cancellationToken))
            throw Failure(NativeFailureCode.WindowRestoreFailed, "restoreWindow", "verifyRestore", target, "Window did not reach the restored postcondition.");
        return true;
    }

    private async Task<bool> ActivateCoreAsync(NativeWindowTarget target, CancellationToken cancellationToken)
    {
        var geometry = api.GetGeometry(target);
        RequireActivationState(geometry);
        if (api.IsForeground(target)) return false;
        api.TryActivate(target);
        if (!await PollAsync(() => api.IsForeground(target), cancellationToken))
            throw Failure(NativeFailureCode.WindowActivationFailed, "activateWindow", "verifyForeground", target, "Windows did not grant foreground activation.");
        return true;
    }

    private static bool ShouldRestore(NativeWindowGeometry geometry, NativeOperationPolicy policy) =>
        geometry.IsMinimized && policy.AllowRestore;
    private bool ShouldActivate(NativeWindowTarget target, NativeOperationPolicy policy) =>
        policy.RequireForeground && policy.AllowActivate && !api.IsForeground(target);
    private void QueueRestore(NativeWindowTarget target)
    {
        if (!api.QueueRestore(target))
            throw Failure(NativeFailureCode.WindowRestoreFailed, "restoreWindow", "queueRestore", target, "Window restore request was rejected.");
    }
    private static void RequireActivationState(NativeWindowGeometry geometry)
    {
        if (!NativeGeometryMath.IsUsableWindow(geometry))
            throw Failure(NativeFailureCode.WindowActivationFailed, "activateWindow", "validateState", geometry.Target, "Window is not visible and restored.");
    }

    private async Task<bool> PollAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)lifecycleTimeout.TotalMilliseconds;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate()) return true;
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        } while (Environment.TickCount64 < deadline);
        return false;
    }

    private void VerifyKeyboard(NativeWindowTarget target)
    {
        var geometry = api.GetGeometry(target);
        if (geometry.IsMinimized || geometry.IsCloaked || !geometry.IsVisible || !api.IsForeground(target))
            throw Failure(NativeFailureCode.ForegroundRequired, "keyboard", "verifyForeground", target, "Target is not usable and foreground.");
        if (!api.HasOwnedKeyboardFocus(target))
            throw Failure(NativeFailureCode.ForeignKeyboardFocus, "keyboard", "verifyFocus", target, "Keyboard focus is not owned by the attached window/process.");
    }

    private void VerifyKeyboardAfterDispatch(NativeWindowTarget target, uint count)
    {
        try { VerifyKeyboard(target); }
        catch (NativeOperationException exception)
        {
            throw new NativeOperationException(exception.Code, exception.Operation, "afterDispatch",
                "Keyboard input may already have occurred; target foreground or focus could not be verified after dispatch. Do not retry blindly.",
                exception.Target ?? target, exception.Win32Error)
            {
                ForegroundTarget = exception.ForegroundTarget,
                DispatchedInputCount = count
            };
        }
    }

    private static void ValidatePolicy(NativeOperationPolicy policy, bool keyboard)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.Enabled || (keyboard ? !policy.AllowKeyboard : !policy.AllowMouse))
            throw new NativeOperationException(NativeFailureCode.RawInputDisabled, "nativeInput", "authorize", "Native input capability is disabled.");
        if (!Enum.IsDefined(policy.ConstrainTo) || policy.AllowedMouseButtons is null ||
            policy.MaximumTextLength is < 1 or > 10000 || (policy.AllowKeyboard && !policy.RequireForeground))
            throw new NativeOperationException(NativeFailureCode.InvalidArgument, "nativeInput", "validatePolicy", "Native operation policy is invalid.");
    }

    private static bool IsWellFormedUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i])) return false;
            }
            else if (char.IsLowSurrogate(text[i])) return false;
        }
        return true;
    }

    private async Task<T> SerializedAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await AcquireGateAsync(cancellationToken);
        try { return await operation(); }
        catch (NativeOperationException exception)
        {
            AddForegroundDiagnostic(exception);
            throw;
        }
        finally { OperationGate.Release(); }
    }

    private async Task AcquireGateAsync(CancellationToken cancellationToken)
    {
        if (!await OperationGate.WaitAsync(lifecycleTimeout, cancellationToken))
            throw new NativeOperationException(NativeFailureCode.InputDispatchFailed, "nativeDesktop", "waitForBroker",
                "Another native operation has not completed; no input was dispatched.");
    }
    private void AddForegroundDiagnostic(NativeOperationException exception)
    {
        try { exception.ForegroundTarget = api.GetForegroundTarget(); }
        catch (NativeOperationException) { /* Preserve the original failure on unsupported platforms. */ }
    }

    internal static NativeOperationException Failure(NativeFailureCode code, string operation, string phase,
        NativeWindowTarget target, string message, uint? dispatched = null) =>
        new(code, operation, phase, message, target) { DispatchedInputCount = dispatched };
}
