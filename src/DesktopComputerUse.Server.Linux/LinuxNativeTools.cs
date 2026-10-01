using System.ComponentModel;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DesktopComputerUse.Server.Linux;

[McpServerToolType]
public sealed class LinuxNativeTools
{
    [McpServerTool(Name = "reload_application_profiles", UseStructuredContent = true)]
    [Description("Atomically reloads portable application profiles; invalid edits preserve the last valid registry.")]
    public static AutomationResult<ApplicationProfileReloadResult> ReloadApplicationProfiles(PortableProfileStore profiles)
    {
        var result = profiles.Reload();
        return new(result.Succeeded, result, result.Succeeded ? null :
            new AutomationError(AutomationErrorCode.InvalidProfile,
                "Profile reload was rejected; the previous valid registry remains active. See value.failures."));
    }

    [McpServerTool(Name = "get_desktop_layout", UseStructuredContent = true)]
    [Description("Windows monitor geometry is unavailable on Linux.")]
    public static AutomationResult<NativeDesktopLayout> GetDesktopLayout() => Unsupported<NativeDesktopLayout>();

    [McpServerTool(Name = "get_window_geometry", UseStructuredContent = true)]
    [Description("Windows HWND geometry is unavailable on Linux.")]
    public static AutomationResult<NativeWindowGeometry> GetWindowGeometry() => Unsupported<NativeWindowGeometry>();

    [McpServerTool(Name = "restore_window", UseStructuredContent = true)]
    [Description("Windows window restoration is unavailable on Linux.")]
    public static AutomationResult<NativeLifecycleResult> RestoreWindow() => Unsupported<NativeLifecycleResult>();

    [McpServerTool(Name = "activate_window", UseStructuredContent = true)]
    [Description("Windows foreground activation is unavailable on Linux.")]
    public static AutomationResult<NativeLifecycleResult> ActivateWindow(bool restoreIfMinimized = false)
        => Unsupported<NativeLifecycleResult>();

    [McpServerTool(Name = "click_at_point", UseStructuredContent = true)]
    [Description("Windows native mouse input is unavailable on Linux.")]
    public static AutomationResult<NativeClickResult> ClickAtPoint(
        int x, int y, string coordinateSpace = "physicalVirtualScreen", string button = "left")
        => Unsupported<NativeClickResult>();

    [McpServerTool(Name = "type_text", UseStructuredContent = true)]
    [Description("Windows native keyboard input is unavailable on Linux.")]
    public static AutomationResult<NativeKeyboardResult> TypeText(string text) => Unsupported<NativeKeyboardResult>();

    [McpServerTool(Name = "key_press", UseStructuredContent = true)]
    [Description("Windows native keyboard input is unavailable on Linux.")]
    public static AutomationResult<NativeKeyboardResult> KeyPress(string chord) => Unsupported<NativeKeyboardResult>();

    [McpServerTool(Name = "click_capture_point", UseStructuredContent = true)]
    [Description("Windows capture-relative mouse input is unavailable on Linux.")]
    public static AutomationResult<NativeClickResult> ClickCapturePoint(
        string captureId, int x, int y, string button = "left") => Unsupported<NativeClickResult>();

    [McpServerTool(Name = "capture_application_window_image")]
    [Description("Windows HWND capture is unavailable on Linux.")]
    public static IReadOnlyList<ContentBlock> CaptureApplicationWindowImage()
        => throw new McpException($"{AutomationErrorCode.PlatformNotSupported}: {PlatformMessage}");

    private const string PlatformMessage = "Native Windows desktop operations require the Windows release in an interactive desktop.";
    private static AutomationResult<T> Unsupported<T>()
        => AutomationResult<T>.Failure(AutomationErrorCode.PlatformNotSupported, PlatformMessage);
}
