using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class AutomationExceptionResultMapperTests
{
    [Theory]
    [InlineData(NativeFailureCode.ForegroundRequired)]
    [InlineData(NativeFailureCode.ForeignKeyboardFocus)]
    public void Postdispatch_keyboard_failure_warns_about_input_and_disables_automatic_retry(NativeFailureCode code)
    {
        var exception = new NativeOperationException(code, "typeText", "afterDispatch", "private keyboard payload",
            new(42, 123))
        {
            DispatchedInputCount = 4, ForegroundTarget = new(99, 987)
        };
        var error = AutomationExceptionResultMapper.MapError(exception, false);
        Assert.Equal("afterDispatch", error.Diagnostic?.Phase);
        Assert.Equal(4u, error.Diagnostic?.DispatchedInputCount);
        Assert.Equal(99, error.Diagnostic?.ForegroundProcessId);
        Assert.Equal(987, error.Diagnostic?.ForegroundHwnd);
        Assert.True(error.Diagnostic?.InputMayHaveOccurred);
        Assert.False(error.Diagnostic?.Retryable);
        Assert.Contains("input may already have occurred", error.Message);
        Assert.Contains("Do not retry automatically", error.Message);
        Assert.DoesNotContain("private keyboard payload", error.Message);
    }

    [Theory]
    [InlineData("afterDispatch", null, true)]
    [InlineData("afterDispatch", 0, true)]
    [InlineData("dispatch", 2, true)]
    [InlineData("verifyForeground", null, false)]
    [InlineData("verifyForeground", 0, false)]
    [InlineData("dispatch", 0, false)]
    public void Native_input_uncertainty_uses_dispatch_phase_and_count(string phase, int? count, bool mayHaveOccurred)
    {
        var exception = new NativeOperationException(NativeFailureCode.ForegroundRequired, "keyPress", phase, "private")
        {
            DispatchedInputCount = count is int value ? (uint)value : null
        };
        var error = AutomationExceptionResultMapper.MapError(exception, false);
        Assert.Equal(mayHaveOccurred, error.Diagnostic?.InputMayHaveOccurred);
        Assert.Equal(!mayHaveOccurred, error.Diagnostic?.Retryable);
        Assert.Equal(exception.DispatchedInputCount, error.Diagnostic?.DispatchedInputCount);
        Assert.Equal(phase, error.Diagnostic?.Phase);
        Assert.Equal(mayHaveOccurred, error.Message.Contains("input may already have occurred", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(NativeFailureCode.PlatformNotSupported, AutomationErrorCode.PlatformNotSupported)]
    [InlineData(NativeFailureCode.DpiAwarenessRequired, AutomationErrorCode.DpiAwarenessRequired)]
    [InlineData(NativeFailureCode.InvalidWindow, AutomationErrorCode.InvalidWindow)]
    [InlineData(NativeFailureCode.RawInputDisabled, AutomationErrorCode.RawInputDisabled)]
    [InlineData(NativeFailureCode.InvalidArgument, AutomationErrorCode.InvalidCoordinates)]
    [InlineData(NativeFailureCode.PointOutsideTarget, AutomationErrorCode.InvalidCoordinates)]
    [InlineData(NativeFailureCode.ForeignWindowAtPoint, AutomationErrorCode.ForeignWindowAtPoint)]
    [InlineData(NativeFailureCode.ForegroundRequired, AutomationErrorCode.ForegroundChanged)]
    [InlineData(NativeFailureCode.ForeignKeyboardFocus, AutomationErrorCode.FocusNotOwned)]
    [InlineData(NativeFailureCode.WindowRestoreFailed, AutomationErrorCode.WindowRestoreFailed)]
    [InlineData(NativeFailureCode.WindowActivationFailed, AutomationErrorCode.WindowActivationFailed)]
    [InlineData(NativeFailureCode.InputDispatchFailed, AutomationErrorCode.InputDispatchFailed)]
    [InlineData(NativeFailureCode.CaptureDisabled, AutomationErrorCode.CaptureDisabled)]
    [InlineData(NativeFailureCode.CaptureFailed, AutomationErrorCode.CaptureFailed)]
    [InlineData(NativeFailureCode.UnsafeScreenCapture, AutomationErrorCode.UnsafeScreenCapture)]
    [InlineData(NativeFailureCode.SensitiveGeometryUnavailable, AutomationErrorCode.RedactionFailed)]
    [InlineData(NativeFailureCode.StaleCapture, AutomationErrorCode.StaleCapture)]
    [InlineData((NativeFailureCode)999, AutomationErrorCode.NativeOperationFailed)]
    public void Native_failures_map_to_shared_error_codes(NativeFailureCode native, AutomationErrorCode expected)
    {
        var result = AutomationExceptionResultMapper.Map<string>(
            new NativeOperationException(native, "nativeOperation", "validation", "secret detail"), false);
        Assert.Equal(expected, result.Error?.Code);
        Assert.Equal(expected, result.Error?.Diagnostic?.Code);
        Assert.Equal("nativeOperation", result.Error?.Diagnostic?.Operation);
        Assert.Equal("validation", result.Error?.Diagnostic?.Phase);
        Assert.Equal(nameof(NativeOperationException), result.Error?.Diagnostic?.ExceptionType);
        Assert.DoesNotContain("secret", result.Error!.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }

    [Fact]
    public void Native_diagnostics_preserve_target_foreground_and_dispatch_details()
    {
        var exception = new NativeOperationException(NativeFailureCode.InputDispatchFailed,
            "typeText", "dispatch", "private payload", new(42, 123), 5)
        {
            ForegroundTarget = new(99, 987), DispatchedInputCount = 3
        };
        var context = new AutomationDiagnostic
        {
            ProcessId = 10, Hwnd = 20, ProfileId = "profile", ProfileRevision = "revision", CorrelationId = "native"
        };
        var diagnostic = AutomationExceptionResultMapper.CreateDiagnostic(exception, context);
        Assert.Equal(42, diagnostic.ProcessId);
        Assert.Equal(123, diagnostic.Hwnd);
        Assert.Equal(99, diagnostic.ForegroundProcessId);
        Assert.Equal(987, diagnostic.ForegroundHwnd);
        Assert.Equal(3u, diagnostic.DispatchedInputCount);
        Assert.Equal(5, diagnostic.Win32Error);
        Assert.Equal("profile", diagnostic.ProfileId);
        Assert.Equal("revision", diagnostic.ProfileRevision);
        Assert.Equal("native", diagnostic.CorrelationId);
        Assert.Equal($"0x{unchecked((uint)exception.HResult):X8}", diagnostic.HResult);
        Assert.False(diagnostic.Retryable);
        Assert.True(diagnostic.InputMayHaveOccurred);
    }

    [Theory]
    [InlineData(NativeFailureCode.StaleCapture)]
    [InlineData(NativeFailureCode.ForegroundRequired)]
    [InlineData(NativeFailureCode.ForeignKeyboardFocus)]
    [InlineData(NativeFailureCode.WindowActivationFailed)]
    [InlineData(NativeFailureCode.WindowRestoreFailed)]
    public void Native_transient_failures_are_retryable_and_fall_back_to_context_target(NativeFailureCode code)
    {
        var exception = new NativeOperationException(code, "operation", "phase", "private");
        var diagnostic = AutomationExceptionResultMapper.CreateDiagnostic(exception,
            new AutomationDiagnostic { ProcessId = 42, Hwnd = 123 });
        Assert.True(diagnostic.Retryable);
        Assert.False(diagnostic.InputMayHaveOccurred);
        Assert.Equal(42, diagnostic.ProcessId);
        Assert.Equal(123, diagnostic.Hwnd);
        Assert.Null(diagnostic.ForegroundProcessId);
        Assert.Null(diagnostic.ForegroundHwnd);
        Assert.Null(diagnostic.DispatchedInputCount);
        Assert.Null(diagnostic.Win32Error);
        var noContext = AutomationExceptionResultMapper.CreateDiagnostic(exception);
        Assert.Null(noContext.ProcessId);
        Assert.Null(noContext.Hwnd);
    }

    [Theory]
    [InlineData(5, AutomationErrorCode.AccessDenied)]
    [InlineData(1460, AutomationErrorCode.Timeout)]
    [InlineData(87, AutomationErrorCode.NativeOperationFailed)]
    public void Classify_native_error_codes(int error, AutomationErrorCode expected)
        => Assert.Equal(expected, AutomationExceptionResultMapper.Classify(new System.ComponentModel.Win32Exception(error)));

    [Theory]
    [InlineData(0x800705B4, AutomationErrorCode.Timeout)]
    [InlineData(0x8001011F, AutomationErrorCode.Timeout)]
    [InlineData(0x80010001, AutomationErrorCode.ProviderFailure)]
    [InlineData(0x8001010A, AutomationErrorCode.ProviderFailure)]
    public void Provider_timeout_and_busy_failures_are_retryable(uint hresult, AutomationErrorCode code)
    {
        var result = AutomationExceptionResultMapper.Map<string>(
            new System.Runtime.InteropServices.COMException("private", unchecked((int)hresult)), false);
        Assert.Equal(code, result.Error?.Code);
        Assert.True(result.Error?.Diagnostic?.Retryable);
        Assert.NotNull(result.Error?.Diagnostic?.CorrelationId);
    }

    [Fact]
    public void Caller_cancellation_is_not_retryable()
    {
        var result = AutomationExceptionResultMapper.Map<string>(new OperationCanceledException(), true);
        Assert.Equal(AutomationErrorCode.OperationCancelled, result.Error?.Diagnostic?.Code);
        Assert.False(result.Error?.Diagnostic?.Retryable);
        Assert.Equal("The automation operation was cancelled.", result.Error?.Message);
    }

    [Fact]
    public void Diagnostic_context_preserves_correlation_and_operation_identity()
    {
        var context = new AutomationDiagnostic
        {
            CorrelationId = "correlation", Operation = "launch", Phase = "startup",
            ProfileId = "profile", ProfileRevision = "revision", ProcessId = 42, Hwnd = -123,
            CandidateId = "node-0002", Depth = 3, CleanupOutcome = "terminated"
        };
        var diagnostic = AutomationExceptionResultMapper.CreateDiagnostic(new TimeoutException(), context);
        Assert.Equal("correlation", diagnostic.CorrelationId);
        Assert.Equal("launch", diagnostic.Operation);
        Assert.Equal("startup", diagnostic.Phase);
        Assert.Equal("profile", diagnostic.ProfileId);
        Assert.Equal("revision", diagnostic.ProfileRevision);
        Assert.Equal(42, diagnostic.ProcessId);
        Assert.Equal(-123, diagnostic.Hwnd);
        Assert.Equal("node-0002", diagnostic.CandidateId);
        Assert.Equal(3, diagnostic.Depth);
        Assert.Equal("terminated", diagnostic.CleanupOutcome);
        Assert.Equal("TimeoutException", diagnostic.ExceptionType);
        Assert.Null(diagnostic.Win32Error);
        Assert.True(diagnostic.Retryable);
    }

    [Theory]
    [InlineData("ElementNotAvailableException", AutomationErrorCode.ElementNotAvailable)]
    [InlineData("ElementNotEnabledException", AutomationErrorCode.ElementNotEnabled)]
    [InlineData("PropertyNotSupportedException", AutomationErrorCode.PropertyNotSupported)]
    [InlineData("PatternNotSupportedException", AutomationErrorCode.PropertyNotSupported)]
    public void Provider_exception_names_are_classified(string name, AutomationErrorCode expected)
    {
        var type = typeof(AutomationExceptionResultMapperTests).GetNestedType(name,
            System.Reflection.BindingFlags.NonPublic)!;
        var exception = (Exception)Activator.CreateInstance(type)!;
        Assert.Equal(expected, AutomationExceptionResultMapper.Classify(exception));
    }

    [Fact]
    public void Expected_provider_errors_are_distinguished_from_programming_errors()
    {
        Assert.True(AutomationExceptionResultMapper.IsExpectedProviderException(new TimeoutException()));
        Assert.True(AutomationExceptionResultMapper.IsExpectedProviderException(new UnauthorizedAccessException()));
        Assert.True(AutomationExceptionResultMapper.IsExpectedProviderException(new System.ComponentModel.Win32Exception()));
        Assert.True(AutomationExceptionResultMapper.IsExpectedProviderException(new System.Runtime.InteropServices.COMException()));
        Assert.False(AutomationExceptionResultMapper.IsExpectedProviderException(new InvalidOperationException()));
        Assert.False(AutomationExceptionResultMapper.IsExpectedProviderException(new OperationCanceledException()));
    }

    [Theory]
    [InlineData("System.Windows.Automation.ElementNotAvailableException", AutomationErrorCode.ElementNotAvailable)]
    [InlineData("System.Windows.Automation.ElementNotEnabledException", AutomationErrorCode.ElementNotEnabled)]
    [InlineData("FlaUI.Core.Exceptions.CustomProviderException", AutomationErrorCode.ProviderFailure)]
    public void Known_provider_type_metadata_is_recognized_without_loading_windows_runtime(string fullName, AutomationErrorCode code)
    {
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName($"SyntheticProvider-{Guid.NewGuid():N}"),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("Provider").DefineType(
            fullName, System.Reflection.TypeAttributes.Public, typeof(Exception)).CreateType()!;
        var exception = (Exception)Activator.CreateInstance(type)!;
        Assert.True(AutomationExceptionResultMapper.IsExpectedProviderException(exception));
        Assert.Equal(code, AutomationExceptionResultMapper.Classify(exception));
    }

    private sealed class ElementNotAvailableException : Exception { }
    private sealed class ElementNotEnabledException : Exception { }
    private sealed class PropertyNotSupportedException : Exception { }
    private sealed class PatternNotSupportedException : Exception { }

    [Theory]
    [InlineData(0x80070005, AutomationErrorCode.AccessDenied)]
    [InlineData(0x80040201, AutomationErrorCode.ElementNotAvailable)]
    [InlineData(0x80040200, AutomationErrorCode.ElementNotEnabled)]
    [InlineData(0x80040204, AutomationErrorCode.PropertyNotSupported)]
    [InlineData(0x80131505, AutomationErrorCode.Timeout)]
    [InlineData(0x80004005, AutomationErrorCode.ProviderFailure)]
    public void Map_classifies_com_hresult(uint hresult, AutomationErrorCode expected)
    {
        var result = AutomationExceptionResultMapper.Map<string>(
            new System.Runtime.InteropServices.COMException("sensitive detail", unchecked((int)hresult)), false,
            new AutomationDiagnostic { Operation = "inspectControls", Phase = "enumerateChildren" });
        Assert.Equal(expected, result.Error?.Code);
        Assert.Equal(expected, result.Error?.Diagnostic?.Code);
        Assert.Equal("inspectControls", result.Error?.Diagnostic?.Operation);
        Assert.Equal("enumerateChildren", result.Error?.Diagnostic?.Phase);
        Assert.DoesNotContain("sensitive", result.Error!.Message);
    }

    [Fact]
    public void Map_preserves_structured_operation_diagnostics()
    {
        var diagnostic = new AutomationDiagnostic { Operation = "findControl", CandidateId = "node-0002" };
        var result = AutomationExceptionResultMapper.Map<string>(
            new AutomationOperationException(AutomationErrorCode.ControlNotFound, "not found",
                diagnostic: diagnostic, failures: [diagnostic]), false);
        Assert.Equal(diagnostic with { Code = AutomationErrorCode.ControlNotFound }, result.Error?.Diagnostic);
        Assert.Same(diagnostic, Assert.Single(result.Error!.Failures));
    }

    [Fact]
    public void Map_classifies_native_error_and_preserves_native_code()
    {
        var result = AutomationExceptionResultMapper.Map<string>(
            new System.ComponentModel.Win32Exception(87, "private detail"), false);
        Assert.Equal(AutomationErrorCode.NativeOperationFailed, result.Error?.Code);
        Assert.Equal(87, result.Error?.Diagnostic?.Win32Error);
        Assert.DoesNotContain("private", result.Error!.Message);
    }

    [Fact]
    public void Non_caller_cancellation_is_timeout()
    {
        var result = AutomationExceptionResultMapper.Map<string>(new OperationCanceledException(), false);
        Assert.Equal(AutomationErrorCode.Timeout, result.Error?.Code);
        Assert.Equal("The automation operation timed out.", result.Error?.Message);
    }

    [Fact]
    public void Map_preserves_typed_automation_error()
    {
        var result = AutomationExceptionResultMapper.Map<string>(
            new AutomationOperationException(
                AutomationErrorCode.AmbiguousControl,
                "ambiguous",
                []),
            callerCancelled: false);

        Assert.Equal(AutomationErrorCode.AmbiguousControl, result.Error?.Code);
        Assert.Equal("ambiguous", result.Error?.Message);
    }

    [Theory]
    [InlineData(typeof(TimeoutException), false, AutomationErrorCode.Timeout)]
    [InlineData(typeof(OperationCanceledException), true, AutomationErrorCode.OperationCancelled)]
    [InlineData(typeof(UnauthorizedAccessException), false, AutomationErrorCode.AccessDenied)]
    [InlineData(typeof(InvalidOperationException), false, AutomationErrorCode.AutomationFailure)]
    public void Map_classifies_standard_exceptions(
        Type exceptionType,
        bool callerCancelled,
        AutomationErrorCode expected)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "failure")!;

        var result = AutomationExceptionResultMapper.Map<string>(
            exception,
            callerCancelled);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error?.Message));
    }
}
