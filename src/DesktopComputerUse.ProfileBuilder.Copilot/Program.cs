using DesktopComputerUse.Automation.Windows;

if (OperatingSystem.IsWindows())
{
    Win32DesktopApi.InitializePerMonitorV2();
}

return await DesktopComputerUse.ProfileBuilder.Copilot.CopilotProfileBuilderCli.RunAsync(args);
