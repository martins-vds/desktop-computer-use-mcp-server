using System.Drawing;
using System.Drawing.Imaging;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Windows;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using Microsoft.Extensions.Logging;

namespace DesktopComputerUse.Automation;

public sealed partial class DesktopAutomationController
{
    private readonly WindowsDesktopBroker _desktop = new();
    private IWindowCaptureProvider? _windowCapture;
    private IWindowCaptureProvider WindowCaptureProvider =>
        _windowCapture ??= new WindowsWindowCaptureProvider(_desktop);

    public Task<AutomationResult<ApplicationState>> LaunchAsync(
        string profileId, string ifAlreadyRunning, string onLaunchFailure,
        CancellationToken cancellationToken)
    {
        if (onLaunchFailure != "terminateSpawned")
        {
            return Task.FromResult(AutomationResult<ApplicationState>.Failure(
                AutomationErrorCode.ApplicationNotAllowed,
                "onLaunchFailure must be terminateSpawned; preserving unreported spawned processes is not supported."));
        }

        if (!TryParseLaunchPolicy(ifAlreadyRunning, out var policy))
        {
            return Task.FromResult(AutomationResult<ApplicationState>.Failure(
                AutomationErrorCode.ApplicationNotAllowed,
                "ifAlreadyRunning must be fail, attach, or launchNew."));
        }

        return LaunchAsync(profileId, policy, cancellationToken);
    }

    internal static bool TryParseLaunchPolicy(string? value, out LaunchPolicy policy)
    {
        var selected = value switch
        {
            "fail" => (LaunchPolicy?)LaunchPolicy.Fail,
            "attach" => LaunchPolicy.Attach,
            "launchNew" => LaunchPolicy.LaunchNew,
            _ => null
        };
        policy = selected.GetValueOrDefault();
        return selected.HasValue;
    }

    public async Task<AutomationResult<ApplicationProfileReloadResult>> ReloadProfilesAsync(CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(DefaultTimeout, cancellationToken, token =>
        {
            token.ThrowIfCancellationRequested();
            var result = ReloadableProfiles().Reload();
            _logger.LogInformation("AUDIT action=reloadProfiles succeeded={Succeeded} generation={Generation}",
                result.Succeeded, result.Generation);
            return result;
        });
        if (result.Value is { Succeeded: false } reload)
        {
            return new AutomationResult<ApplicationProfileReloadResult>(false, reload,
                new AutomationError(AutomationErrorCode.InvalidProfile,
                    "Profile reload was rejected; the previous valid registry remains active. See value.failures."));
        }

        return result;
    }

    private IReloadableApplicationProfileStore ReloadableProfiles()
        => _profiles as IReloadableApplicationProfileStore
            ?? throw new AutomationOperationException(AutomationErrorCode.InvalidProfile,
                "The configured profile store does not support reload.");

    public Task<AutomationResult<NativeDesktopLayout>> GetDesktopLayoutAsync(CancellationToken cancellationToken)
        => ExecuteAsync(DefaultTimeout, cancellationToken, token =>
        {
            token.ThrowIfCancellationRequested();
            EnsureWindows();
            return _desktop.GetDesktopLayout();
        });

    public Task<AutomationResult<NativeWindowGeometry>> GetWindowGeometryAsync(CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(cancellationToken, (session, _) =>
        {
            var geometry = _desktop.GetWindowGeometry(NativeTarget(session));
            var summary = _observer.Observe(session.MainWindow, session.Profile);
            return AddUiaBounds(geometry, summary.Bounds);
        });

    internal static NativeWindowGeometry AddUiaBounds(NativeWindowGeometry geometry, RectangleInfo uia)
    {
        var native = geometry.WindowBounds;
        return geometry with
        {
            UiaBounds = uia,
            Warnings = uia.X != native.X || uia.Y != native.Y ||
                uia.Width != native.Width || uia.Height != native.Height
                ? ["UIA bounds differ from native window bounds; use native bounds for input."]
                : []
        };
    }

    public Task<AutomationResult<NativeLifecycleResult>> RestoreWindowAsync(CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(cancellationToken, (session, token) =>
            _desktop.RestoreWindowAsync(NativeTarget(session), token).GetAwaiter().GetResult());

    public Task<AutomationResult<NativeLifecycleResult>> ActivateWindowAsync(
        bool restoreIfMinimized, CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(cancellationToken, (session, token) =>
            _desktop.ActivateWindowAsync(NativeTarget(session), restoreIfMinimized, token).GetAwaiter().GetResult());

    public Task<AutomationResult<NativeClickResult>> ClickAtPointAsync(
        int x, int y, string coordinateSpace, string button, CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(cancellationToken, (session, token) =>
        {
            var policy = NativePolicy(session);
            var target = NativeTarget(session);
            var mouseButton = ParseMouseButton(button);
            var result = coordinateSpace switch
            {
                "physicalVirtualScreen" => _desktop.ClickScreenPointAsync(target,
                    new PhysicalScreenPoint(x, y), policy, mouseButton, token).GetAwaiter().GetResult(),
                "windowClient" => _desktop.ClickClientPointAsync(target,
                    new WindowClientPoint(x, y), policy, mouseButton, token).GetAwaiter().GetResult(),
                _ => throw new NativeOperationException(NativeFailureCode.InvalidArgument,
                    "clickAtPoint", "coordinates", "Coordinate space must be physicalVirtualScreen or windowClient.")
            };
            LogAudit("clickAtPoint", session.Profile.Id, target.ProcessId, true, coordinateSpace);
            return result;
        });

    public Task<AutomationResult<NativeKeyboardResult>> TypeTextAsync(string text, CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(cancellationToken, (session, token) =>
        {
            var result = _desktop.TypeTextAsync(NativeTarget(session), text, NativePolicy(session), token)
                .GetAwaiter().GetResult();
            LogAudit("typeText", session.Profile.Id, session.Application.ProcessId, true);
            return result;
        });

    public Task<AutomationResult<NativeKeyboardResult>> KeyPressAsync(string chord, CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(cancellationToken, (session, token) =>
        {
            var result = _desktop.KeyPressAsync(NativeTarget(session), chord, NativePolicy(session), token)
                .GetAwaiter().GetResult();
            LogAudit("keyPress", session.Profile.Id, session.Application.ProcessId, true);
            return result;
        });

    public Task<AutomationResult<NativeClickResult>> ClickCapturePointAsync(
        string captureId, int x, int y, string button, CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(cancellationToken, (session, token) =>
        {
            var policy = NativePolicy(session);
            var target = NativeTarget(session);
            var generation = CaptureGeneration(session);
            var capture = WindowCaptureProvider.GetCaptureToken(captureId, target, generation);
            var result = _desktop.ClickCapturePointAsync(target, capture, generation,
                new CaptureImagePoint(x, y), policy, ParseMouseButton(button), token)
                .GetAwaiter().GetResult();
            LogAudit("clickCapturePoint", session.Profile.Id, target.ProcessId, true, captureId);
            return result;
        });

    private NativeWindowCapture CaptureWindow(AutomationSession session, CancellationToken token)
    {
        var target = NativeTarget(session);
        var geometry = _desktop.GetWindowGeometry(target);
        var sensitiveBounds = CapturePrivacy.ReadSensitiveBounds(session.MainWindow, session.Profile, token);
        RequireCaptureGeometry(geometry, _desktop.GetWindowGeometry(target));
        var capture = WindowCaptureProvider.CaptureAsync(target, new NativeCaptureOptions
        {
            EnableScreenshots = session.Profile.EnableScreenshots,
            SensitiveGeometryComplete = true,
            SensitiveRegions = sensitiveBounds.Select(bounds => new SensitiveCaptureRegion(
                CapturePrivacy.ToPhysicalRegion(bounds))).ToArray(),
            Generation = CaptureGeneration(session),
            ExpectedGeometry = geometry
        }, token).GetAwaiter().GetResult();
        RequireCaptureGeometry(geometry, _desktop.GetWindowGeometry(target));
        var after = CapturePrivacy.ReadSensitiveBounds(session.MainWindow, session.Profile, token);
        if (!sensitiveBounds.OrderBy(bounds => (bounds.X, bounds.Y, bounds.Width, bounds.Height))
                .SequenceEqual(after.OrderBy(bounds => (bounds.X, bounds.Y, bounds.Width, bounds.Height))))
        {
            throw new NativeOperationException(NativeFailureCode.SensitiveGeometryUnavailable,
                "captureWindow", "verifyRedactions", "Sensitive-control geometry changed during capture.");
        }

        return capture;
    }

    internal static void RequireCaptureGeometry(NativeWindowGeometry expected, NativeWindowGeometry actual)
    {
        if (expected.WindowBounds != actual.WindowBounds || expected.ClientBounds != actual.ClientBounds ||
            expected.Dpi != actual.Dpi)
        {
            throw new NativeOperationException(NativeFailureCode.StaleCapture, "captureWindow",
                "verifyGeometry", "Window geometry changed while reading redaction information.");
        }
    }

    private WindowCapture CaptureWindowResult(AutomationSession session, CancellationToken token)
    {
        var capture = CaptureWindow(session, token);
        return new WindowCapture(capture.MimeType, Convert.ToBase64String(capture.ImageBytes),
            capture.ImageWidth, capture.ImageHeight)
        {
            Method = capture.Method,
            OcclusionSafe = capture.OcclusionSafe,
            Token = capture.Token,
            RedactedControlCount = capture.RedactedControlCount
        };
    }

    private WindowCapture CaptureControlResult(
        AutomationSession session, ControlSelector selector, CancellationToken token)
    {
        var element = ResolveSingle(session, selector, token);
        if (IsSensitiveElement(element, session.Profile))
        {
            throw new AutomationOperationException(AutomationErrorCode.ApplicationNotAllowed,
                "Capturing a password or sensitive control is not permitted.");
        }

        var bounds = element.BoundingRectangle;
        var capture = CaptureWindow(session, token);
        if (bounds != element.BoundingRectangle)
        {
            throw new NativeOperationException(NativeFailureCode.StaleCapture,
                "captureControl", "verifyControlGeometry", "Control geometry changed while capturing.");
        }

        var crop = MapControlCrop(
            new PhysicalScreenRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), capture.Token.Transform);
        using var stream = new MemoryStream(capture.ImageBytes);
        using var bitmap = new Bitmap(stream);
        using var cropped = bitmap.Clone(new Rectangle(crop.X, crop.Y, crop.Width, crop.Height), bitmap.PixelFormat);
        using var output = new MemoryStream();
        cropped.Save(output, ImageFormat.Png);
        // A cropped control image deliberately has no clickable full-window capture transform.
        return new WindowCapture("image/png", Convert.ToBase64String(output.ToArray()), cropped.Width, cropped.Height)
        {
            Method = capture.Method + "/controlCrop",
            OcclusionSafe = true,
            RedactedControlCount = capture.RedactedControlCount
        };
    }

    internal static PhysicalScreenRect MapControlCrop(PhysicalScreenRect bounds, CapturePixelTransform transform)
    {
        try
        {
            return NativeGeometryMath.MapSensitiveRegion(bounds, transform);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new NativeOperationException(NativeFailureCode.CaptureFailed, "captureControl",
                "validateCrop", "The control has no reliable geometry inside the captured window.");
        }
    }

    private static NativeWindowTarget NativeTarget(AutomationSession session)
        => new(session.Application.ProcessId, session.NativeWindowHandle);

    private RectangleInfo NativeRootBounds(AutomationSession session)
    {
        var bounds = _desktop.GetWindowGeometry(NativeTarget(session)).WindowBounds;
        return new RectangleInfo(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static NativeCaptureGeneration CaptureGeneration(AutomationSession session)
        => new(session.SessionId, session.Profile.Metadata?.Revision ?? session.SessionId, 0);

    private static NativeOperationPolicy NativePolicy(AutomationSession session)
    {
        var policy = session.Profile.NativeInput;
        if (!policy.Enabled)
        {
            throw new NativeOperationException(NativeFailureCode.RawInputDisabled,
                "nativeInput", "authorize", "Native input is disabled by the attached profile.");
        }

        return new NativeOperationPolicy
        {
            Enabled = policy.Enabled,
            AllowMouse = policy.AllowMouse,
            AllowKeyboard = policy.AllowKeyboard,
            AllowedMouseButtons = policy.AllowedMouseButtons.Select(ParseMouseButton).ToArray(),
            ConstrainTo = policy.ConstrainTo == "clientArea" ? NativeInputBounds.ClientArea : NativeInputBounds.Window,
            RequireForeground = policy.RequireForeground,
            MaximumTextLength = policy.MaximumTextLength,
            AllowSystemKeys = policy.AllowSystemKeys
        };
    }

    internal static NativeMouseButton ParseMouseButton(string? button) => button switch
    {
        "left" => NativeMouseButton.Left,
        "right" => NativeMouseButton.Right,
        "middle" => NativeMouseButton.Middle,
        _ => throw new NativeOperationException(NativeFailureCode.InvalidArgument,
            "nativeInput", "button", "Button must be left, right, or middle.")
    };

    private void InvalidateCaptures(AutomationSession session)
        => _windowCapture?.InvalidateSession(session.SessionId);
}
