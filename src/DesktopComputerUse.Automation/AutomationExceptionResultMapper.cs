using System.ComponentModel;
using System.Runtime.InteropServices;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation;

public static class AutomationExceptionResultMapper
{
    private static readonly IReadOnlyDictionary<NativeFailureCode, AutomationErrorCode> NativeCodes =
        new Dictionary<NativeFailureCode, AutomationErrorCode>
        {
            [NativeFailureCode.PlatformNotSupported] = AutomationErrorCode.PlatformNotSupported,
            [NativeFailureCode.DpiAwarenessRequired] = AutomationErrorCode.DpiAwarenessRequired,
            [NativeFailureCode.InvalidWindow] = AutomationErrorCode.InvalidWindow,
            [NativeFailureCode.RawInputDisabled] = AutomationErrorCode.RawInputDisabled,
            [NativeFailureCode.InvalidArgument] = AutomationErrorCode.InvalidCoordinates,
            [NativeFailureCode.PointOutsideTarget] = AutomationErrorCode.InvalidCoordinates,
            [NativeFailureCode.ForeignWindowAtPoint] = AutomationErrorCode.ForeignWindowAtPoint,
            [NativeFailureCode.ForegroundRequired] = AutomationErrorCode.ForegroundChanged,
            [NativeFailureCode.ForeignKeyboardFocus] = AutomationErrorCode.FocusNotOwned,
            [NativeFailureCode.WindowRestoreFailed] = AutomationErrorCode.WindowRestoreFailed,
            [NativeFailureCode.WindowActivationFailed] = AutomationErrorCode.WindowActivationFailed,
            [NativeFailureCode.InputDispatchFailed] = AutomationErrorCode.InputDispatchFailed,
            [NativeFailureCode.CaptureDisabled] = AutomationErrorCode.CaptureDisabled,
            [NativeFailureCode.CaptureFailed] = AutomationErrorCode.CaptureFailed,
            [NativeFailureCode.UnsafeScreenCapture] = AutomationErrorCode.UnsafeScreenCapture,
            [NativeFailureCode.SensitiveGeometryUnavailable] = AutomationErrorCode.RedactionFailed,
            [NativeFailureCode.StaleCapture] = AutomationErrorCode.StaleCapture
        };

    public static AutomationResult<T> Map<T>(
        Exception exception,
        bool callerCancelled,
        AutomationDiagnostic? context = null)
        => new(false, default, MapError(exception, callerCancelled, context));

    public static AutomationError MapError(
        Exception exception,
        bool callerCancelled,
        AutomationDiagnostic? context = null)
    {
        var code = exception is AutomationOperationException automation
            ? automation.Code
            : Classify(exception, callerCancelled);
        var message = exception switch
        {
            AutomationOperationException typed => typed.Message,
            NativeOperationException native => NativeErrorMessage(native),
            TimeoutException => "The automation operation timed out.",
            OperationCanceledException => callerCancelled
                ? "The automation operation was cancelled."
                : "The automation operation timed out.",
            UnauthorizedAccessException => "Windows denied access to the target process or control.",
            _ => "The automation operation failed. See the structured diagnostic for details."
        };
        var diagnostic = (exception as AutomationOperationException)?.Diagnostic ??
            CreateDiagnostic(exception, context);
        var error = new AutomationError(code, message,
            (exception as AutomationOperationException)?.Candidates)
        {
            Diagnostic = diagnostic with
            {
                Code = code,
                Retryable = code != AutomationErrorCode.OperationCancelled &&
                    diagnostic.Retryable
            },
            Failures = (exception as AutomationOperationException)?.Failures ?? []
        };
        return error;
    }

    public static AutomationDiagnostic CreateDiagnostic(
        Exception exception,
        AutomationDiagnostic? context = null)
        => exception is NativeOperationException native
            ? CreateNativeDiagnostic(native, context)
            : (context ?? new AutomationDiagnostic()) with
        {
            Code = Classify(exception),
            ExceptionType = exception.GetType().Name,
            HResult = $"0x{unchecked((uint)exception.HResult):X8}",
            Win32Error = exception is Win32Exception win32 ? win32.NativeErrorCode : null,
            Retryable = Classify(exception) == AutomationErrorCode.Timeout ||
                unchecked((uint)exception.HResult) is 0x80010001 or 0x8001010A
        };

    public static bool IsExpectedProviderException(Exception exception)
        => exception is COMException or TimeoutException or UnauthorizedAccessException or Win32Exception ||
            exception.GetType().Namespace?.StartsWith("FlaUI.Core.Exceptions", StringComparison.Ordinal) == true ||
            exception.GetType().FullName is
                "System.Windows.Automation.ElementNotAvailableException" or
                "System.Windows.Automation.ElementNotEnabledException";

    public static AutomationErrorCode Classify(Exception exception, bool callerCancelled = false)
    {
        if (exception is AutomationOperationException typed)
            return typed.Code;
        if (exception is NativeOperationException native)
            return NativeCodes.TryGetValue(native.Code, out var code) ? code : AutomationErrorCode.NativeOperationFailed;
        if (exception is OperationCanceledException)
            return callerCancelled ? AutomationErrorCode.OperationCancelled : AutomationErrorCode.Timeout;
        return ClassifyHResult(unchecked((uint)exception.HResult)) ?? ClassifyType(exception);
    }

    private static AutomationDiagnostic CreateNativeDiagnostic(NativeOperationException exception, AutomationDiagnostic? context)
        => (context ?? new AutomationDiagnostic()) with
        {
            Code = Classify(exception),
            Operation = exception.Operation,
            Phase = exception.Phase,
            ExceptionType = nameof(NativeOperationException),
            HResult = $"0x{unchecked((uint)exception.HResult):X8}",
            Win32Error = exception.Win32Error,
            ProcessId = exception.Target?.ProcessId ?? context?.ProcessId,
            Hwnd = exception.Target?.WindowHandle ?? context?.Hwnd,
            ForegroundProcessId = exception.ForegroundTarget?.ProcessId,
            ForegroundHwnd = exception.ForegroundTarget?.WindowHandle,
            DispatchedInputCount = exception.DispatchedInputCount,
            InputMayHaveOccurred = InputMayHaveOccurred(exception),
            Retryable = CanRetryNative(exception)
        };

    private static string NativeErrorMessage(NativeOperationException exception)
        => InputMayHaveOccurred(exception)
            ? "The native desktop operation failed after input may already have occurred. Do not retry automatically; inspect the application state."
            : "The native desktop operation was rejected or failed. See the structured diagnostic for details.";

    private static bool InputMayHaveOccurred(NativeOperationException exception)
        => exception.DispatchedInputCount is > 0 || exception.Phase == "afterDispatch";

    private static bool CanRetryNative(NativeOperationException exception) =>
        !InputMayHaveOccurred(exception) &&
        exception.Code is NativeFailureCode.StaleCapture or NativeFailureCode.ForegroundRequired or
            NativeFailureCode.ForeignKeyboardFocus or NativeFailureCode.WindowActivationFailed or NativeFailureCode.WindowRestoreFailed;

    private static AutomationErrorCode? ClassifyHResult(uint hresult)
        => hresult switch
        {
            0x80131505 or 0x800705B4 or 0x8001011F => AutomationErrorCode.Timeout,
            0x80070005 => AutomationErrorCode.AccessDenied,
            0x80040201 => AutomationErrorCode.ElementNotAvailable,
            0x80040200 => AutomationErrorCode.ElementNotEnabled,
            0x80040204 => AutomationErrorCode.PropertyNotSupported,
            _ => null
        };

    private static AutomationErrorCode ClassifyType(Exception exception)
        => exception switch
        {
            TimeoutException or Win32Exception { NativeErrorCode: 1460 } => AutomationErrorCode.Timeout,
            UnauthorizedAccessException or Win32Exception { NativeErrorCode: 5 } => AutomationErrorCode.AccessDenied,
            Win32Exception => AutomationErrorCode.NativeOperationFailed,
            COMException => AutomationErrorCode.ProviderFailure,
            _ => ClassifyProviderType(exception)
        };

    private static AutomationErrorCode ClassifyProviderType(Exception exception)
        => exception.GetType().Name switch
        {
            "ElementNotAvailableException" => AutomationErrorCode.ElementNotAvailable,
            "ElementNotEnabledException" => AutomationErrorCode.ElementNotEnabled,
            "PropertyNotSupportedException" or "PatternNotSupportedException" => AutomationErrorCode.PropertyNotSupported,
            _ => IsExpectedProviderException(exception)
                ? AutomationErrorCode.ProviderFailure : AutomationErrorCode.AutomationFailure
        };
}
