namespace DesktopComputerUse.Contracts.Automation;

public sealed record WindowCapture(
    string MimeType,
    string Base64Data,
    int Width,
    int Height);
