using DesktopComputerUse.Contracts.Configuration;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;

namespace DesktopComputerUse.Automation.FlaUi;

internal sealed class AutomationSession : IDisposable
{
    public AutomationSession(
        ApplicationProfile profile,
        Application application,
        AutomationBase automation,
        Window mainWindow,
        bool ownsProcess)
    {
        Profile = profile;
        Application = application;
        Automation = automation;
        MainWindow = mainWindow;
        OwnsProcess = ownsProcess;
    }

    public ApplicationProfile Profile { get; }

    public Application Application { get; }

    public AutomationBase Automation { get; }

    public Window MainWindow { get; set; }

    public bool OwnsProcess { get; }

    public void Dispose()
    {
        Automation.Dispose();
        Application.Dispose();
    }
}
