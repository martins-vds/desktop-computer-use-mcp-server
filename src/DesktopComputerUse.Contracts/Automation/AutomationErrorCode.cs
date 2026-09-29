namespace DesktopComputerUse.Contracts.Automation;

public enum AutomationErrorCode
{
    PlatformNotSupported,
    ProfileNotFound,
    InvalidProfile,
    ApplicationNotAllowed,
    ExecutableNotFound,
    ProcessNotFound,
    ProcessExited,
    ApplicationAlreadyAttached,
    ApplicationNotAttached,
    WindowNotFound,
    ControlNotFound,
    AmbiguousControl,
    UnsupportedPattern,
    ReadOnlyControl,
    Timeout,
    AccessDenied,
    OperationCancelled,
    AutomationFailure
}
