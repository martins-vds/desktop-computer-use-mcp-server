using DesktopComputerUse.Automation;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Native.Tests;

public sealed class NativeExceptionMappingTests
{
    public static TheoryData<NativeFailureCode, AutomationErrorCode> Mappings => new()
    {
        { NativeFailureCode.PlatformNotSupported, AutomationErrorCode.PlatformNotSupported },
        { NativeFailureCode.DpiAwarenessRequired, AutomationErrorCode.DpiAwarenessRequired },
        { NativeFailureCode.InvalidWindow, AutomationErrorCode.InvalidWindow },
        { NativeFailureCode.RawInputDisabled, AutomationErrorCode.RawInputDisabled },
        { NativeFailureCode.InvalidArgument, AutomationErrorCode.InvalidCoordinates },
        { NativeFailureCode.PointOutsideTarget, AutomationErrorCode.InvalidCoordinates },
        { NativeFailureCode.ForeignWindowAtPoint, AutomationErrorCode.ForeignWindowAtPoint },
        { NativeFailureCode.ForegroundRequired, AutomationErrorCode.ForegroundChanged },
        { NativeFailureCode.ForeignKeyboardFocus, AutomationErrorCode.FocusNotOwned },
        { NativeFailureCode.WindowRestoreFailed, AutomationErrorCode.WindowRestoreFailed },
        { NativeFailureCode.WindowActivationFailed, AutomationErrorCode.WindowActivationFailed },
        { NativeFailureCode.InputDispatchFailed, AutomationErrorCode.InputDispatchFailed },
        { NativeFailureCode.CaptureDisabled, AutomationErrorCode.CaptureDisabled },
        { NativeFailureCode.CaptureFailed, AutomationErrorCode.CaptureFailed },
        { NativeFailureCode.UnsafeScreenCapture, AutomationErrorCode.UnsafeScreenCapture },
        { NativeFailureCode.SensitiveGeometryUnavailable, AutomationErrorCode.RedactionFailed },
        { NativeFailureCode.StaleCapture, AutomationErrorCode.StaleCapture }
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public void EveryNativeFailureHasAnExplicitPublicMapping(NativeFailureCode nativeCode, AutomationErrorCode expected)
    {
        var exception = new NativeOperationException(nativeCode, "nativeOperation", "nativePhase", "private secret",
            new(42, -123), 87) { ForegroundTarget = new(99, 567), DispatchedInputCount = 1 };
        var context = new AutomationDiagnostic
        {
            Operation = "toolOperation", Phase = "toolPhase", ProfileId = "profile",
            ProfileRevision = "revision", CorrelationId = "correlation", ProcessId = 1, Hwnd = 2
        };
        var result = AutomationExceptionResultMapper.Map<string>(exception, false, context);
        Assert.False(result.Succeeded);
        var error = Assert.IsType<AutomationError>(result.Error);
        var diagnostic = Assert.IsType<AutomationDiagnostic>(error.Diagnostic);
        Assert.Equal(expected, error.Code);
        Assert.Equal(expected, diagnostic.Code);
        Assert.Equal(expected, AutomationExceptionResultMapper.Classify(exception));
        Assert.Equal("nativeOperation", diagnostic.Operation);
        Assert.Equal("nativePhase", diagnostic.Phase);
        Assert.Equal(42, diagnostic.ProcessId);
        Assert.Equal(-123, diagnostic.Hwnd);
        Assert.Equal(87, diagnostic.Win32Error);
        Assert.Equal(99, diagnostic.ForegroundProcessId);
        Assert.Equal(567, diagnostic.ForegroundHwnd);
        Assert.Equal(1U, diagnostic.DispatchedInputCount);
        Assert.Equal("profile", diagnostic.ProfileId);
        Assert.Equal("revision", diagnostic.ProfileRevision);
        Assert.Equal("correlation", diagnostic.CorrelationId);
        Assert.Equal(nameof(NativeOperationException), diagnostic.ExceptionType);
        Assert.StartsWith("0x", diagnostic.HResult);
        Assert.NotEmpty(error.Message);
        Assert.Empty(error.Failures);
        Assert.False(diagnostic.Retryable);
        Assert.DoesNotContain("private", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public void MappingTableCoversEveryDeclaredNativeCode() =>
        Assert.Equal(Enum.GetValues<NativeFailureCode>().Order(), Mappings.Select(row => (NativeFailureCode)row[0]).Order());

    [Fact]
    public void ContextIsRetainedWhenNativeTargetIsUnavailableAndPartialInputIsNotRetryable()
    {
        var exception = new NativeOperationException(NativeFailureCode.InputDispatchFailed,
            "typeText", "sendInput", "private") { DispatchedInputCount = 1 };
        var diagnostic = AutomationExceptionResultMapper.CreateDiagnostic(exception,
            new AutomationDiagnostic { ProcessId = 123, Hwnd = 456, ProfileRevision = "revision" });
        Assert.Equal(123, diagnostic.ProcessId);
        Assert.Equal(456, diagnostic.Hwnd);
        Assert.Equal("revision", diagnostic.ProfileRevision);
        Assert.Null(diagnostic.ForegroundProcessId);
        Assert.Null(diagnostic.ForegroundHwnd);
        Assert.Null(diagnostic.Win32Error);
        Assert.Equal(1U, diagnostic.DispatchedInputCount);
        Assert.False(diagnostic.Retryable);
        Assert.Equal("caller", (diagnostic with { Operation = "caller" }).Operation);
    }

    [Fact]
    public void UnknownNativeCodeFailsSafely()
    {
        var error = AutomationExceptionResultMapper.MapError(new NativeOperationException(
            (NativeFailureCode)999, "unknown", "phase", "secret"), false);
        Assert.Equal(AutomationErrorCode.NativeOperationFailed, error.Code);
        Assert.False(error.Diagnostic!.Retryable);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public void NativeDiagnosticWithoutContextOrTargetLeavesIdentityUnset()
    {
        var diagnostic = AutomationExceptionResultMapper.CreateDiagnostic(
            new NativeOperationException(NativeFailureCode.RawInputDisabled, "click", "authorize", "secret"));
        Assert.Null(diagnostic.ProcessId);
        Assert.Null(diagnostic.Hwnd);
        Assert.Null(diagnostic.ProfileId);
        Assert.Null(diagnostic.ProfileRevision);
        Assert.NotEmpty(diagnostic.CorrelationId);
        Assert.False(diagnostic.Retryable);
    }

    [Fact]
    public void NonNativeHresultStillUsesExactHexRepresentation()
    {
        var diagnostic = AutomationExceptionResultMapper.CreateDiagnostic(
            new System.Runtime.InteropServices.COMException("secret", unchecked((int)0x80070005)));
        Assert.Equal("0x80070005", diagnostic.HResult);
        Assert.Equal(AutomationErrorCode.AccessDenied, diagnostic.Code);
    }

    [Theory]
    [InlineData(NativeFailureCode.StaleCapture)]
    [InlineData(NativeFailureCode.ForegroundRequired)]
    [InlineData(NativeFailureCode.ForeignKeyboardFocus)]
    [InlineData(NativeFailureCode.WindowActivationFailed)]
    [InlineData(NativeFailureCode.WindowRestoreFailed)]
    public void RecoveryIsRetryableOnlyBeforeAnyInputWasDispatched(NativeFailureCode code)
    {
        var beforeInput = new NativeOperationException(code, "operation", "phase", "message");
        Assert.True(AutomationExceptionResultMapper.CreateDiagnostic(beforeInput).Retryable);
        var zeroInput = new NativeOperationException(code, "operation", "phase", "message") { DispatchedInputCount = 0 };
        Assert.True(AutomationExceptionResultMapper.CreateDiagnostic(zeroInput).Retryable);
        var afterInput = new NativeOperationException(code, "operation", "phase", "message") { DispatchedInputCount = 1 };
        Assert.False(AutomationExceptionResultMapper.CreateDiagnostic(afterInput).Retryable);
    }
}
