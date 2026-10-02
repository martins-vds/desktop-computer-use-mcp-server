namespace DesktopComputerUse.Contracts.Automation;

public sealed record NativeCaptureGeneration(string SessionId, string ProfileRevision, long WindowGeneration);

/// <summary>In privacy mode, callers must enumerate all sensitive descendants even when bounds cannot be read.</summary>
public sealed record SensitiveCaptureRegion(PhysicalScreenRect? Bounds);

public sealed record NativeCaptureOptions
{
    public bool PrivacyMode { get; init; } = true;
    public bool EnableScreenshots { get; init; }
    public bool AllowScreenFallback { get; init; }
    public bool SensitiveGeometryComplete { get; init; }
    public IReadOnlyList<SensitiveCaptureRegion> SensitiveRegions { get; init; } = [];
    public NativeCaptureGeneration Generation { get; init; } = new("", "", 0);
    public NativeWindowGeometry? ExpectedGeometry { get; init; }
}

public sealed record CapturePixelTransform(PhysicalScreenRect SourceRect, int ImageWidth, int ImageHeight);
public sealed record NativeCaptureToken(
    string CaptureId, DateTimeOffset Timestamp, NativeWindowTarget Target,
    NativeCaptureGeneration Generation, NativeWindowGeometry Geometry, CapturePixelTransform Transform);

public sealed record NativeWindowCapture(
    byte[] ImageBytes, string MimeType, string Method, bool OcclusionSafe,
    NativeCaptureToken Token, int RedactedControlCount)
{
    public bool PrivacyMode { get; init; } = true;
    public uint Dpi => Token.Geometry.Dpi;
    public PhysicalScreenRect SourceScreenRect => Token.Transform.SourceRect;
    public int ImageWidth => Token.Transform.ImageWidth;
    public int ImageHeight => Token.Transform.ImageHeight;
    public bool Restored => false;
    public bool Activated => false;
}
