using System.ComponentModel;
using DesktopComputerUse.Automation;
using DesktopComputerUse.Contracts.Automation;
using ModelContextProtocol.Server;

namespace DesktopComputerUse.Server.Tools;

[McpServerToolType]
public sealed class ControlTools
{
    [McpServerTool(Name = "inspect_controls", UseStructuredContent = true)]
    [Description("Returns a bounded, redacted control tree inside the attached application window.")]
    public static Task<AutomationResult<ControlTreeNode>> InspectControls(
        DesktopAutomationController controller,
        [Description("Optional control to use as the inspection root. Omit it to inspect from the attached main window.")] ControlSelector? rootSelector = null,
        [Description("Optional depth capped by the selected application profile.")] int? maxDepth = null,
        CancellationToken cancellationToken = default)
        => controller.InspectControlsAsync(rootSelector, maxDepth, cancellationToken);

    [McpServerTool(Name = "find_control", UseStructuredContent = true)]
    [Description("Resolves exactly one control inside the attached application. Ambiguous selectors fail with compact candidate summaries.")]
    public static Task<AutomationResult<ControlSummary>> FindControl(
        DesktopAutomationController controller,
        [Description("A semantic profile key or explicit automation ID, name, control type, class, ancestor, and optional zero-based index.")] ControlSelector selector,
        CancellationToken cancellationToken)
        => controller.FindControlAsync(selector, cancellationToken);

    [McpServerTool(Name = "get_control_properties", UseStructuredContent = true)]
    [Description("Returns compact, redacted properties and supported semantic patterns for one control.")]
    public static Task<AutomationResult<ControlSummary>> GetControlProperties(
        DesktopAutomationController controller,
        ControlSelector selector,
        CancellationToken cancellationToken)
        => controller.GetControlPropertiesAsync(selector, cancellationToken);

    [McpServerTool(Name = "invoke_control", UseStructuredContent = true)]
    [Description("Invokes a button, menu item, or similar control through its UI Automation Invoke pattern.")]
    public static Task<AutomationResult<ActionResult>> InvokeControl(
        DesktopAutomationController controller,
        ControlSelector selector,
        CancellationToken cancellationToken)
        => controller.InvokeAsync(selector, cancellationToken);

    [McpServerTool(Name = "set_control_value", UseStructuredContent = true)]
    [Description("Sets a writable control through its UI Automation Value pattern and verifies the observed state. The supplied value is not written to the audit log.")]
    public static Task<AutomationResult<ActionResult>> SetControlValue(
        DesktopAutomationController controller,
        ControlSelector selector,
        [Description("The value to set. This value is never written to the audit log.")] string value,
        CancellationToken cancellationToken)
        => controller.SetValueAsync(selector, value, cancellationToken);

    [McpServerTool(Name = "select_control_item", UseStructuredContent = true)]
    [Description("Selects a list, tab, tree, radio, or similar item through its UI Automation SelectionItem pattern.")]
    public static Task<AutomationResult<ActionResult>> SelectControlItem(
        DesktopAutomationController controller,
        ControlSelector selector,
        CancellationToken cancellationToken)
        => controller.SelectAsync(selector, cancellationToken);

    [McpServerTool(Name = "set_expanded_state", UseStructuredContent = true)]
    [Description("Expands or collapses a control through its UI Automation ExpandCollapse pattern.")]
    public static Task<AutomationResult<ActionResult>> SetExpandedState(
        DesktopAutomationController controller,
        ControlSelector selector,
        [Description("True to expand the control; false to collapse it.")] bool expanded,
        CancellationToken cancellationToken)
        => controller.SetExpandedStateAsync(selector, expanded, cancellationToken);

    [McpServerTool(Name = "scroll_control", UseStructuredContent = true)]
    [Description("Scrolls a control semantically in bounded small increments. Each axis accepts values from -20 to 20.")]
    public static Task<AutomationResult<ActionResult>> ScrollControl(
        DesktopAutomationController controller,
        ControlSelector selector,
        int horizontalSteps = 0,
        int verticalSteps = 0,
        CancellationToken cancellationToken = default)
        => controller.ScrollAsync(
            selector,
            horizontalSteps,
            verticalSteps,
            cancellationToken);

    [McpServerTool(Name = "wait_for_state", UseStructuredContent = true)]
    [Description("Waits for one bounded control existence, text, value, enabled, or visibility condition without using an arbitrary fixed delay.")]
    public static Task<AutomationResult<ControlSummary>> WaitForState(
        DesktopAutomationController controller,
        ControlSelector selector,
        WaitCondition condition,
        [Description("Optional timeout capped by the application profile's operation timeout.")] int? timeoutMs = null,
        CancellationToken cancellationToken = default)
        => controller.WaitForStateAsync(
            selector,
            condition,
            timeoutMs,
            cancellationToken);
}
