using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation;

public static class AutomationExceptionResultMapper
{
    private static readonly IReadOnlyDictionary<Type, Func<Exception, AutomationError>> StandardMappings =
        new Dictionary<Type, Func<Exception, AutomationError>>
        {
            [typeof(TimeoutException)] = exception => new(
                AutomationErrorCode.Timeout,
                exception.Message),
            [typeof(OperationCanceledException)] = _ => new(
                AutomationErrorCode.OperationCancelled,
                "The automation operation was cancelled."),
            [typeof(UnauthorizedAccessException)] = _ => new(
                AutomationErrorCode.AccessDenied,
                "Windows denied access to the target process or control.")
        };

    public static AutomationResult<T> Map<T>(
        Exception exception,
        bool callerCancelled)
    {
        _ = callerCancelled;
        if (exception is AutomationOperationException automation)
        {
            return AutomationResult<T>.Failure(
                automation.Code,
                automation.Message,
                automation.Candidates);
        }

        var error = StandardMappings.TryGetValue(exception.GetType(), out var mapping)
            ? mapping(exception)
            : new AutomationError(
                AutomationErrorCode.AutomationFailure,
                "The automation operation failed unexpectedly. See the server log for details.");
        return AutomationResult<T>.Failure(error.Code, error.Message, error.Candidates);
    }
}
