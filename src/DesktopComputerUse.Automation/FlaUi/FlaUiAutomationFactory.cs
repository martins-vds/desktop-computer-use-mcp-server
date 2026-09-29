using DesktopComputerUse.Contracts.Automation;
using FlaUI.Core;
using FlaUI.UIA2;
using FlaUI.UIA3;

namespace DesktopComputerUse.Automation.FlaUi;

public sealed class FlaUiAutomationFactory
{
    public AutomationBase Create(AutomationBackend backend)
        => backend switch
        {
            AutomationBackend.Uia2 => new UIA2Automation(),
            AutomationBackend.Uia3 => new UIA3Automation(),
            _ => throw new AutomationOperationException(
                AutomationErrorCode.InvalidProfile,
                $"Unsupported automation backend '{backend}'.")
        };
}
