using System.ComponentModel;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using ModelContextProtocol.Server;

namespace DesktopComputerUse.Server.Linux;

[McpServerToolType]
public sealed class LinuxApplicationTools
{
    [McpServerTool(Name = "list_application_profiles", UseStructuredContent = true)]
    [Description("Lists configured application profiles. Native Windows automation is unavailable in the Linux release.")]
    public static IReadOnlyList<ApplicationProfileSummary> ListApplicationProfiles(
        PortableProfileStore profiles)
        => profiles.List();

    [McpServerTool(Name = "launch_application", UseStructuredContent = true)]
    [Description("Returns a platform error because Windows Forms automation is unavailable on Linux.")]
    public static AutomationResult<ApplicationState> LaunchApplication(
        string profileId,
        string ifAlreadyRunning = "fail",
        string onLaunchFailure = "terminateSpawned")
        => Unsupported<ApplicationState>();

    [McpServerTool(Name = "attach_application", UseStructuredContent = true)]
    [Description("Returns a platform error because Windows process attachment is unavailable on Linux.")]
    public static AutomationResult<ApplicationState> AttachApplication(
        string profileId,
        int processId)
        => Unsupported<ApplicationState>();

    [McpServerTool(Name = "detach_application", UseStructuredContent = true)]
    [Description("Returns a platform error because no Windows automation session can exist on Linux.")]
    public static AutomationResult DetachApplication(bool terminateOwnedProcess = false)
        => AutomationResult.Failure(
            AutomationErrorCode.PlatformNotSupported,
            PlatformMessage);

    [McpServerTool(Name = "get_application_state", UseStructuredContent = true)]
    [Description("Returns a platform error because Windows Forms automation is unavailable on Linux.")]
    public static AutomationResult<ApplicationState> GetApplicationState()
        => Unsupported<ApplicationState>();

    [McpServerTool(Name = "capture_application_window", UseStructuredContent = true)]
    [Description("Returns a platform error because Windows application capture is unavailable on Linux.")]
    public static AutomationResult<WindowCapture> CaptureApplicationWindow()
        => Unsupported<WindowCapture>();

    private const string PlatformMessage =
        "Native Windows Forms automation is only available in the Windows release. Use the Linux executable for MCP discovery and profile validation only.";

    private static AutomationResult<T> Unsupported<T>()
        => AutomationResult<T>.Failure(
            AutomationErrorCode.PlatformNotSupported,
            PlatformMessage);
}
