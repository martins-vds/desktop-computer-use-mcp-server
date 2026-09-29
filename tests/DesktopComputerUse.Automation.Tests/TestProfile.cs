using DesktopComputerUse.Contracts.Configuration;

namespace DesktopComputerUse.Automation.Tests;

internal static class TestProfile
{
    public static ApplicationProfile Create(
        string id = "test-app",
        string? executablePath = null)
        => new()
        {
            Id = id,
            DisplayName = "Test application",
            ExecutablePath = executablePath ??
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-app.exe"))
        };
}
