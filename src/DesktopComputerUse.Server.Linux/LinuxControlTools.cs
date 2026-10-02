using System.ComponentModel;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Resolution;
using DesktopComputerUse.Contracts.Profiles;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DesktopComputerUse.Server.Linux;

[McpServerToolType]
public sealed class LinuxControlTools
{
    [McpServerTool(Name = "capture_control_image")]
    [Description(PlatformMessage)]
    public static IReadOnlyList<ContentBlock> CaptureControlImage(ControlSelector selector)
        => throw new McpException(PlatformMessage);

    [McpServerTool(Name = "snapshot_application_schema", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ApplicationSnapshot> SnapshotApplicationSchema(
        int? maxDepth = null,
        int? maxResults = null)
        => Unsupported<ApplicationSnapshot>();

    [McpServerTool(Name = "resolve_control_intent", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ControlResolutionResult> ResolveControlIntent(
        string semanticKey,
        int maximumCandidates = 10)
        => Unsupported<ControlResolutionResult>();

    [McpServerTool(Name = "get_profile_update_proposals", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<IReadOnlyList<ProfileUpdateProposal>> GetProfileUpdateProposals()
        => Unsupported<IReadOnlyList<ProfileUpdateProposal>>();

    [McpServerTool(Name = "export_profile_update_patch", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ProfileUpdatePatch> ExportProfileUpdatePatch(string proposalId)
        => Unsupported<ProfileUpdatePatch>();

    [McpServerTool(Name = "inspect_controls", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ControlTreeNode> InspectControls(
        ControlSelector? rootSelector = null,
        int? maxDepth = null)
        => Unsupported<ControlTreeNode>();

    [McpServerTool(Name = "find_control", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ControlSummary> FindControl(ControlSelector selector)
        => Unsupported<ControlSummary>();

    [McpServerTool(Name = "get_control_properties", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ControlSummary> GetControlProperties(ControlSelector selector)
        => Unsupported<ControlSummary>();

    [McpServerTool(Name = "get_control_value", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ControlValueResult> GetControlValue(ControlSelector selector)
        => Unsupported<ControlValueResult>();

    [McpServerTool(Name = "invoke_control", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ActionResult> InvokeControl(ControlSelector selector)
        => Unsupported<ActionResult>();

    [McpServerTool(Name = "set_control_value", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ActionResult> SetControlValue(
        ControlSelector selector,
        string value)
        => Unsupported<ActionResult>();

    [McpServerTool(Name = "select_control_item", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ActionResult> SelectControlItem(ControlSelector selector)
        => Unsupported<ActionResult>();

    [McpServerTool(Name = "set_expanded_state", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ActionResult> SetExpandedState(
        ControlSelector selector,
        bool expanded)
        => Unsupported<ActionResult>();

    [McpServerTool(Name = "scroll_control", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ActionResult> ScrollControl(
        ControlSelector selector,
        int horizontalSteps = 0,
        int verticalSteps = 0)
        => Unsupported<ActionResult>();

    [McpServerTool(Name = "wait_for_state", UseStructuredContent = true)]
    [Description(PlatformMessage)]
    public static AutomationResult<ControlSummary> WaitForState(
        ControlSelector selector,
        WaitCondition condition,
        int? timeoutMs = null)
        => Unsupported<ControlSummary>();

    private const string PlatformMessage =
        "Native Windows Forms control automation is unavailable in the Linux release.";

    private static AutomationResult<T> Unsupported<T>()
        => AutomationResult<T>.Failure(
            AutomationErrorCode.PlatformNotSupported,
            PlatformMessage);
}
