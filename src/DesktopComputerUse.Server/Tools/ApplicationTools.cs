using System.ComponentModel;
using DesktopComputerUse.Automation;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using ModelContextProtocol.Server;

namespace DesktopComputerUse.Server.Tools;

[McpServerToolType]
public sealed class ApplicationTools
{
    [McpServerTool(Name = "list_application_profiles", UseStructuredContent = true)]
    [Description("Lists the configured, allowlisted Windows application profiles.")]
    public static IReadOnlyList<ApplicationProfileSummary> ListApplicationProfiles(
        DesktopAutomationController controller)
        => controller.ListProfiles();

    [McpServerTool(Name = "launch_application", UseStructuredContent = true)]
    [Description("Launches an allowlisted Windows application using a configured profile and attaches automation to its main window.")]
    public static Task<AutomationResult<ApplicationState>> LaunchApplication(
        DesktopAutomationController controller,
        [Description("The ID returned by list_application_profiles.")] string profileId,
        CancellationToken cancellationToken,
        [Description("Existing-process policy: fail (default), attach (one verified match), or launchNew (requires profile permission).")] string ifAlreadyRunning = "fail",
        [Description("Failed launches terminate only the newly spawned process. Supported value: terminateSpawned.")] string onLaunchFailure = "terminateSpawned")
        => controller.LaunchAsync(profileId, ifAlreadyRunning, onLaunchFailure, cancellationToken);

    [McpServerTool(Name = "attach_application", UseStructuredContent = true)]
    [Description("Attaches to an existing process only when its executable matches the selected allowlisted application profile.")]
    public static Task<AutomationResult<ApplicationState>> AttachApplication(
        DesktopAutomationController controller,
        [Description("The ID returned by list_application_profiles.")] string profileId,
        [Description("The Windows process ID to verify and attach.")] int processId,
        CancellationToken cancellationToken)
        => controller.AttachAsync(profileId, processId, cancellationToken);

    [McpServerTool(Name = "detach_application", UseStructuredContent = true)]
    [Description("Releases the active automation session. The application is closed only when it was launched by this server and terminateOwnedProcess is true.")]
    public static Task<AutomationResult> DetachApplication(
        DesktopAutomationController controller,
        [Description("Whether to close a process launched and owned by this server. Attached external processes are never terminated.")] bool terminateOwnedProcess = false,
        CancellationToken cancellationToken = default)
        => controller.DetachAsync(terminateOwnedProcess, cancellationToken);

    [McpServerTool(Name = "get_application_state", UseStructuredContent = true)]
    [Description("Returns the attached process, selected profile, automation backend, ownership, and active main-window state.")]
    public static Task<AutomationResult<ApplicationState>> GetApplicationState(
        DesktopAutomationController controller,
        CancellationToken cancellationToken)
        => controller.GetApplicationStateAsync(cancellationToken);

    [McpServerTool(Name = "capture_application_window", UseStructuredContent = true)]
    [Description("Deprecated structured-base64 compatibility capture. Prefer capture_application_window_image. Requires screenshots enabled and a restored visible HWND; redaction must be complete.")]
    public static Task<AutomationResult<WindowCapture>> CaptureApplicationWindow(
        DesktopAutomationController controller,
        CancellationToken cancellationToken)
        => controller.CaptureApplicationWindowAsync(cancellationToken);
}
