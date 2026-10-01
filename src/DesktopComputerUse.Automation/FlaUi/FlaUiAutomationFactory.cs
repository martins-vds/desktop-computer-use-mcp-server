using DesktopComputerUse.Contracts.Automation;
using FlaUI.Core;
using FlaUI.UIA2;
using FlaUI.UIA3;

namespace DesktopComputerUse.Automation.FlaUi;

public sealed class FlaUiAutomationFactory
{
    public AutomationBase Create(AutomationBackend backend)
    {
        if (backend == AutomationBackend.Uia2)
        {
            return new UIA2Automation();
        }
        if (backend == AutomationBackend.Uia3)
        {
            return new UIA3Automation();
        }
        throw new AutomationOperationException(AutomationErrorCode.InvalidProfile,
            $"Unsupported automation backend '{backend}'.");
    }
}
