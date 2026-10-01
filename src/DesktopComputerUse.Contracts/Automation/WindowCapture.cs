namespace DesktopComputerUse.Contracts.Automation;

public sealed record WindowCapture(
    string MimeType,
    string Base64Data,
    int Width,
    int Height)
{
    public string? Method { get; init; }
    public bool OcclusionSafe { get; init; }
    public NativeCaptureToken? Token { get; init; }
    public int RedactedControlCount { get; init; }
}
