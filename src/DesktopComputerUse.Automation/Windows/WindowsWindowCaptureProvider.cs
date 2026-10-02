using System.Collections.Concurrent;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Windows;

public sealed class WindowsWindowCaptureProvider : IWindowCaptureProvider
{
    private readonly WindowsDesktopBroker broker;
    private readonly INativeWindowPixelSource pixels;
    private readonly TimeSpan timeout;
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<string, NativeCaptureToken> tokens = new();

    public WindowsWindowCaptureProvider(WindowsDesktopBroker broker, INativeWindowPixelSource? pixels = null,
        TimeSpan? timeout = null, TimeProvider? clock = null)
    {
        this.broker = broker;
        this.pixels = pixels ?? new PrintWindowPixelSource();
        this.timeout = timeout ?? TimeSpan.FromSeconds(3);
        this.clock = clock ?? TimeProvider.System;
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<NativeWindowCapture> CaptureAsync(NativeWindowTarget target, NativeCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(target, options);
        // Snapshot mutable caller lists before starting asynchronous native work.
        options = options with { SensitiveRegions = options.SensitiveRegions.ToArray() };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var deadlineToken = deadline.Token;
        var operation = Task.Run(() => CaptureCoreAsync(target, options, deadlineToken), CancellationToken.None);
        try
        {
            return await operation.WaitAsync(deadlineToken);
        }
        catch (OperationCanceledException)
        {
            // PrintWindow is synchronous and cannot safely be aborted. The worker retains the broker gate
            // until it finishes, discards any late pixels, and prevents parallel native dispatch.
            _ = operation.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            cancellationToken.ThrowIfCancellationRequested();
            throw Failure(NativeFailureCode.CaptureFailed, target, "timeout", "Window capture exceeded its bounded timeout.");
        }
    }

    public NativeCaptureToken GetCaptureToken(string captureId, NativeWindowTarget target, NativeCaptureGeneration generation)
    {
        if (!tokens.TryGetValue(captureId, out var token) || token.Timestamp < clock.GetUtcNow().AddMinutes(-5) ||
            !NativeGeometryMath.CaptureIsCurrent(token, target, generation, broker.GetWindowGeometry(target)))
            throw Failure(NativeFailureCode.StaleCapture, target, "validateCapture", "Capture is unknown, expired, or no longer matches the session/window.");
        return token;
    }

    public void InvalidateSession(string sessionId)
    {
        foreach (var item in tokens.Where(item => item.Value.Generation.SessionId == sessionId))
            tokens.TryRemove(item.Key, out _);
    }

    private async Task<NativeWindowCapture> CaptureCoreAsync(NativeWindowTarget target, NativeCaptureOptions options,
        CancellationToken cancellationToken)
    {
        await broker.OperationGate.WaitAsync(cancellationToken);
        try { return CaptureLocked(target, options, cancellationToken); }
        finally { broker.OperationGate.Release(); }
    }

    private NativeWindowCapture CaptureLocked(NativeWindowTarget target, NativeCaptureOptions options, CancellationToken cancellationToken)
    {
        var geometry = broker.GetWindowGeometry(target);
        ValidateExpectedGeometry(target, options.ExpectedGeometry, geometry);
        ValidateCaptureState(target, geometry);
        var source = geometry.WindowBounds;
        var transform = new CapturePixelTransform(source, source.Width, source.Height);
        var redactions = options.PrivacyMode ? MapRedactions(target, options.SensitiveRegions, transform) : [];
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = RequirePixels(target, pixels.TryPrintWindow(target, source), options.AllowScreenFallback);
        cancellationToken.ThrowIfCancellationRequested();
        var token = new NativeCaptureToken(Guid.NewGuid().ToString("N"), clock.GetUtcNow(),
            target, options.Generation, geometry, transform);
        ValidateCapturedGeometry(token, buffer);
        var image = EncodeImage(target, buffer, redactions);
        cancellationToken.ThrowIfCancellationRequested();
        RegisterToken(token);
        return new(image, "image/png", "PrintWindow(PW_RENDERFULLCONTENT)", true, token, redactions.Length)
        {
            PrivacyMode = options.PrivacyMode
        };
    }

    private static void ValidateOptions(NativeWindowTarget target, NativeCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.EnableScreenshots)
            throw Failure(NativeFailureCode.CaptureDisabled, target, "authorize", "Screenshots are disabled.");
        ValidateSensitiveGeometry(target, options);
        ValidateGeneration(target, options.Generation);
    }
    private static void ValidateSensitiveGeometry(NativeWindowTarget target, NativeCaptureOptions options)
    {
        if (options.SensitiveRegions is null || options.PrivacyMode &&
            (!options.SensitiveGeometryComplete || options.SensitiveRegions.Any(region => region is null || region.Bounds is null)))
            throw Failure(NativeFailureCode.SensitiveGeometryUnavailable, target, "redact", "Complete sensitive-control geometry is required.");
    }
    private static void ValidateGeneration(NativeWindowTarget target, NativeCaptureGeneration generation)
    {
        if (generation is null || string.IsNullOrWhiteSpace(generation.SessionId) ||
            string.IsNullOrWhiteSpace(generation.ProfileRevision))
            throw Failure(NativeFailureCode.InvalidArgument, target, "generation", "Session and profile revision are required.");
    }
    private static void ValidateExpectedGeometry(NativeWindowTarget target, NativeWindowGeometry? expected, NativeWindowGeometry actual)
    {
        if (expected is not null && !NativeGeometryMath.SameWindowGeometry(expected, actual))
            throw Failure(NativeFailureCode.SensitiveGeometryUnavailable, target, "validateSourceGeometry", "Window geometry changed since sensitive-control enumeration.");
    }
    private static void ValidateCaptureState(NativeWindowTarget target, NativeWindowGeometry geometry)
    {
        if (!NativeGeometryMath.IsUsableWindow(geometry) || !geometry.WindowBounds.IsNonEmpty)
            throw Failure(NativeFailureCode.CaptureFailed, target, "validateState", "Restore the visible window before capture; capture never implicitly restores or activates.");
    }
    private static PhysicalScreenRect[] MapRedactions(NativeWindowTarget target,
        IReadOnlyList<SensitiveCaptureRegion> regions, CapturePixelTransform transform)
    {
        try { return regions.Select(region => NativeGeometryMath.MapSensitiveRegion(region.Bounds!.Value, transform)).ToArray(); }
        catch (ArgumentOutOfRangeException)
        {
            throw Failure(NativeFailureCode.SensitiveGeometryUnavailable, target, "mapRedactions", "A sensitive descendant cannot be fully mapped to the capture source.");
        }
    }
    private static NativePixelBuffer RequirePixels(NativeWindowTarget target, NativePixelBuffer? buffer, bool allowFallback)
    {
        if (buffer is not null) return buffer;
        // Hit tests cannot prove that screen pixels exclude layered/transparent foreign windows.
        throw Failure(allowFallback ? NativeFailureCode.UnsafeScreenCapture : NativeFailureCode.CaptureFailed,
            target, "printWindow", "PrintWindow failed; screen-region fallback cannot prove occlusion safety and is refused.");
    }
    private void ValidateCapturedGeometry(NativeCaptureToken token, NativePixelBuffer buffer)
    {
        if (buffer.Width != token.Transform.ImageWidth || buffer.Height != token.Transform.ImageHeight ||
            !NativeGeometryMath.CaptureIsCurrent(token, token.Target, token.Generation, broker.GetWindowGeometry(token.Target)))
            throw Failure(NativeFailureCode.StaleCapture, token.Target, "verifyGeometry", "Window geometry changed while capturing.");
    }
    private static byte[] EncodeImage(NativeWindowTarget target, NativePixelBuffer buffer, PhysicalScreenRect[] redactions)
    {
        try { return CaptureImageEncoder.RedactAndEncodePng(buffer, redactions); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw Failure(NativeFailureCode.CaptureFailed, target, "encode", "Native capture buffer could not be safely encoded.");
        }
    }
    private void RegisterToken(NativeCaptureToken token)
    {
        foreach (var item in tokens.Where(item => item.Value.Timestamp < clock.GetUtcNow().AddMinutes(-5)))
            tokens.TryRemove(item.Key, out _);
        while (tokens.Count >= 128) EvictOldestToken();
        tokens[token.CaptureId] = token;
    }
    private void EvictOldestToken()
    {
        var oldest = tokens.OrderBy(item => item.Value.Timestamp).FirstOrDefault();
        if (oldest.Key is not null) tokens.TryRemove(oldest.Key, out _);
    }

    private static NativeOperationException Failure(NativeFailureCode code, NativeWindowTarget target, string phase, string message) =>
        WindowsDesktopBroker.Failure(code, "captureWindow", phase, target, message);
}
