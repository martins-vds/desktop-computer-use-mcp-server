namespace DesktopComputerUse.TestApp;

internal static class NonWindowsStub
{
    public static void Main()
    {
        Console.Error.WriteLine(
            "DesktopComputerUse.TestApp is a Windows-only WinForms integration fixture.");
    }
}
