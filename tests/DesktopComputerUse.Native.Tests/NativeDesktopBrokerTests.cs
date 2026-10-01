using System.Buffers.Binary;
using System.IO.Compression;
using DesktopComputerUse.Automation.Windows;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Native.Tests;

public sealed class NativeDesktopBrokerTests
{
    private static readonly NativeWindowTarget Target = new(123, 456);
    private static readonly NativeCaptureGeneration Generation = new("session-one", "revision", 1);
    private static NativeOperationPolicy MousePolicy => new() { Enabled = true };
    private static NativeOperationPolicy KeyboardPolicy => new() { Enabled = true, AllowKeyboard = true };

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\0b")]
    public async Task MixedOrdinaryAndControlTextNeverDispatches(string text)
    {
        var api = new FakeApi();
        var error = await Assert.ThrowsAsync<NativeOperationException>(() =>
            new WindowsDesktopBroker(api).TypeTextAsync(Target, text, KeyboardPolicy));
        Assert.Equal(NativeFailureCode.InvalidArgument, error.Code);
        Assert.Equal(0, api.InputCalls);
        Assert.Equal(0, api.GeometryReads);
    }

    [Fact]
    public async Task ClientAreaPolicyRejectsWindowChromeButExplicitWindowPolicyAllowsIt()
    {
        var api = new FakeApi();
        var broker = new WindowsDesktopBroker(api);
        var chromePoint = new PhysicalScreenPoint(-995, 115);
        var error = await Assert.ThrowsAsync<NativeOperationException>(() =>
            broker.ClickScreenPointAsync(Target, chromePoint, MousePolicy));
        Assert.Equal(NativeFailureCode.PointOutsideTarget, error.Code);
        Assert.Equal(0, api.InputCalls);
        var result = await broker.ClickScreenPointAsync(Target, chromePoint,
            MousePolicy with { ConstrainTo = NativeInputBounds.Window });
        Assert.Equal(chromePoint, result.RequestedPoint);
        Assert.Equal(1, api.InputCalls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10001)]
    public void LifecycleAndCaptureTimeoutsRejectOutOfRangeValues(int milliseconds)
    {
        var api = new FakeApi();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WindowsDesktopBroker(api, TimeSpan.FromMilliseconds(milliseconds)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WindowsWindowCaptureProvider(new(api), timeout: TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void MaximumTimeoutIsAcceptedWithoutNativeCalls()
    {
        var api = new FakeApi();
        var broker = new WindowsDesktopBroker(api, TimeSpan.FromSeconds(10));
        _ = new WindowsWindowCaptureProvider(broker, timeout: TimeSpan.FromSeconds(10));
        Assert.Equal(0, api.GeometryReads);
    }

    [Fact]
    public void EncoderPreservesDistinctRowsAndRedactsExactlyTheRequestedBottomRightPixel()
    {
        var buffer = new NativePixelBuffer(2, 2, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
        var png = CaptureImageEncoder.RedactAndEncodePng(buffer, [new(1, 1, 1, 1)]);
        Assert.Equal(new byte[]
        {
            0, 3, 2, 1, 255, 7, 6, 5, 255,
            0, 11, 10, 9, 255, 0, 0, 0, 255
        }, DecodePng(png));
    }

    [Fact]
    public void NegativeDesktopAndClientTransformsUsePhysicalPixels()
    {
        var rect = new PhysicalScreenRect(-1920, -200, 1920, 1080);
        Assert.True(rect.Contains(new(-1920, -200)));
        Assert.False(rect.Contains(new(0, 0)));
        Assert.Equal(new PhysicalScreenPoint(-1800, -150), NativeGeometryMath.ClientToScreen(new(120, 50), rect));
        Assert.Equal(new WindowClientPoint(120, 50), NativeGeometryMath.ScreenToClient(new(-1800, -150), rect));
        var absolute = NativeGeometryMath.ToAbsoluteMouse(new(-1920, -200), rect);
        Assert.InRange(absolute.X, 0, 65535);
        Assert.InRange(absolute.Y, 0, 65535);
        Assert.Equal(144, NativeGeometryMath.ScaleLogicalPixels(96, 144));
        Assert.Equal(-2, NativeGeometryMath.ScaleLogicalPixels(-1, 144));
    }

    [Theory]
    [InlineData(-1920, -1080)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(1919, 1079)]
    public void AbsoluteMouseMapsIntoCorrectPixelBin(int x, int y)
    {
        var desktop = new PhysicalScreenRect(-1920, -1080, 3840, 2160);
        var absolute = NativeGeometryMath.ToAbsoluteMouse(new(x, y), desktop);
        Assert.Equal(x, (int)((long)absolute.X * desktop.Width / 65536) + desktop.X);
        Assert.Equal(y, (int)((long)absolute.Y * desktop.Height / 65536) + desktop.Y);
    }

    [Fact]
    public void CaptureAndRedactionTransformsScaleAndRoundOutward()
    {
        var transform = new CapturePixelTransform(new(-1000, 20, 1000, 800), 500, 400);
        Assert.Equal(new PhysicalScreenPoint(-998, 22), NativeGeometryMath.CaptureToScreen(new(1, 1), transform));
        Assert.Equal(new PhysicalScreenRect(0, 0, 2, 2), NativeGeometryMath.MapSensitiveRegion(new(-999, 21, 2, 2), transform));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGeometryMath.CaptureToScreen(new(500, 0), transform));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGeometryMath.MapSensitiveRegion(new(-1001, 21, 2, 2), transform));
    }

    [Fact]
    public async Task DisabledInputDoesNotReadNativeGeometryOrDispatch()
    {
        var api = new FakeApi();
        var exception = await Assert.ThrowsAsync<NativeOperationException>(() =>
            new WindowsDesktopBroker(api).ClickScreenPointAsync(Target, new(-900, 150), new()));
        Assert.Equal(NativeFailureCode.RawInputDisabled, exception.Code);
        AssertStructuredFailure(exception);
        Assert.Equal(0, api.GeometryReads);
        Assert.Equal(0, api.InputCalls);
    }

    [Fact]
    public async Task ForeignOccluderAndOutOfBoundsPointsNeverDispatch()
    {
        var api = new FakeApi { HitBelongsToTarget = false };
        var broker = new WindowsDesktopBroker(api);
        var occluded = await Assert.ThrowsAsync<NativeOperationException>(() =>
            broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy));
        Assert.Equal(NativeFailureCode.ForeignWindowAtPoint, occluded.Code);
        AssertStructuredFailure(occluded);
        var outside = await Assert.ThrowsAsync<NativeOperationException>(() =>
            broker.ClickScreenPointAsync(Target, new(0, 0), MousePolicy));
        Assert.Equal(NativeFailureCode.PointOutsideTarget, outside.Code);
        AssertStructuredFailure(outside);
        Assert.Equal(0, api.InputCalls);
    }

    [Fact]
    public async Task ClickConvertsClientPointAfterRestoreAndActivation()
    {
        var api = new FakeApi { Minimized = true, Foreground = false, MoveOnRestore = true };
        var result = await new WindowsDesktopBroker(api).ClickClientPointAsync(Target, new(10, 10),
            MousePolicy with { AllowRestore = true, AllowActivate = true });
        Assert.Equal(new PhysicalScreenPoint(-790, 160), result.ActualPoint);
        Assert.Equal(1, api.RestoreCalls);
        Assert.Equal(1, api.ActivationCalls);
        Assert.Equal(1, api.InputCalls);
    }

    [Fact]
    public async Task OldScreenPointIsCheckedAgainstPostRestoreGeometry()
    {
        var api = new FakeApi { Minimized = true, MoveOnRestore = true };
        var error = await Assert.ThrowsAsync<NativeOperationException>(() =>
            new WindowsDesktopBroker(api).ClickScreenPointAsync(Target, new(-900, 150),
                MousePolicy with { AllowRestore = true }));
        Assert.Equal(NativeFailureCode.PointOutsideTarget, error.Code);
        Assert.Equal(0, api.InputCalls);
    }

    [Fact]
    public void CaptureGenerationDpiAndDimensionsMustStillMatch()
    {
        var geometry = new FakeApi().GetGeometry(Target);
        var token = new NativeCaptureToken("id", DateTimeOffset.UtcNow, Target, Generation, geometry,
            new(geometry.WindowBounds, geometry.WindowBounds.Width, geometry.WindowBounds.Height));
        Assert.True(NativeGeometryMath.CaptureIsCurrent(token, Target, Generation, geometry));
        Assert.False(NativeGeometryMath.CaptureIsCurrent(token, Target,
            Generation with { ProfileRevision = "changed" }, geometry));
        Assert.False(NativeGeometryMath.CaptureIsCurrent(token, Target,
            Generation with { WindowGeneration = 2 }, geometry));
        Assert.False(NativeGeometryMath.CaptureIsCurrent(token, Target, Generation,
            geometry with { Dpi = 192 }));
        Assert.False(NativeGeometryMath.CaptureIsCurrent(token, Target, Generation,
            geometry with { WindowBounds = geometry.WindowBounds with { Width = 600 } }));
        Assert.False(NativeGeometryMath.CaptureIsCurrent(token, Target, Generation,
            geometry with { IsMinimized = true }));
    }

    [Fact]
    public async Task AsyncRestoreAndForegroundRequestsMustReachPostconditions()
    {
        var api = new FakeApi { Minimized = true, RestoreSucceeds = false };
        var broker = new WindowsDesktopBroker(api, TimeSpan.FromMilliseconds(30));
        var restore = await Assert.ThrowsAsync<NativeOperationException>(() => broker.RestoreWindowAsync(Target));
        Assert.Equal(NativeFailureCode.WindowRestoreFailed, restore.Code);
        AssertStructuredFailure(restore);
        api.Minimized = false;
        api.Foreground = false;
        api.ActivationSucceeds = false;
        var activate = await Assert.ThrowsAsync<NativeOperationException>(() => broker.ActivateWindowAsync(Target));
        Assert.Equal(NativeFailureCode.WindowActivationFailed, activate.Code);
        AssertStructuredFailure(activate);
        Assert.Equal(new NativeWindowTarget(999, 999), activate.ForegroundTarget);
        Assert.Equal(0, api.InputCalls);
    }

    [Theory]
    [InlineData(false, true, NativeFailureCode.ForegroundRequired)]
    [InlineData(true, false, NativeFailureCode.ForeignKeyboardFocus)]
    public async Task KeyboardRequiresForegroundAndOwnedFocus(bool foreground, bool ownedFocus, NativeFailureCode code)
    {
        var api = new FakeApi { Foreground = foreground, OwnedFocus = ownedFocus };
        var broker = new WindowsDesktopBroker(api);
        var text = await Assert.ThrowsAsync<NativeOperationException>(() => broker.TypeTextAsync(Target, "secret", KeyboardPolicy));
        var chord = await Assert.ThrowsAsync<NativeOperationException>(() => broker.KeyPressAsync(Target, "Ctrl+A", KeyboardPolicy));
        Assert.Equal(code, text.Code);
        Assert.Equal(code, chord.Code);
        AssertStructuredFailure(text);
        AssertStructuredFailure(chord);
        Assert.DoesNotContain("secret", text.Message);
        Assert.Equal(0, api.InputCalls);
    }

    [Fact]
    public async Task KeyboardPolicyCannotOptOutOfForegroundAndUnicodeIsBounded()
    {
        var api = new FakeApi();
        var broker = new WindowsDesktopBroker(api);
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.TypeTextAsync(Target, "x", KeyboardPolicy with { RequireForeground = false }));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.TypeTextAsync(Target, "\ud800", KeyboardPolicy));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.TypeTextAsync(Target, "\t", KeyboardPolicy));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.TypeTextAsync(Target, "long", KeyboardPolicy with { MaximumTextLength = 3 }));
        Assert.Equal(0, api.InputCalls);
        var unicode = await broker.TypeTextAsync(Target, "A😀", KeyboardPolicy);
        Assert.Equal(6U, unicode.DispatchedInputCount);
        Assert.True(unicode.IsForeground);
        Assert.Equal(Target.WindowHandle, unicode.WindowHandle);
    }

    [Fact]
    public async Task KeyboardVerifiesForegroundAfterInput()
    {
        var api = new FakeApi { LoseForegroundOnInput = true };
        var exception = await Assert.ThrowsAsync<NativeOperationException>(() =>
            new WindowsDesktopBroker(api).TypeTextAsync(Target, "text", KeyboardPolicy));
        Assert.Equal(NativeFailureCode.ForegroundRequired, exception.Code);
        Assert.Equal("afterDispatch", exception.Phase);
        Assert.Contains("may already have occurred", exception.Message);
        Assert.Equal(8U, exception.DispatchedInputCount);
        Assert.False(DesktopComputerUse.Automation.AutomationExceptionResultMapper.CreateDiagnostic(exception).Retryable);
        Assert.Equal(1, api.InputCalls);
    }

    [Theory]
    [InlineData(false, false, NativeFailureCode.ForegroundRequired, 12U)]
    [InlineData(true, false, NativeFailureCode.ForegroundRequired, 4U)]
    [InlineData(false, true, NativeFailureCode.ForeignKeyboardFocus, 12U)]
    [InlineData(true, true, NativeFailureCode.ForeignKeyboardFocus, 4U)]
    public async Task KeyboardPostDispatchFailuresWarnWithoutLosingDispatchCount(
        bool keyPress, bool loseFocus, NativeFailureCode expectedCode, uint expectedCount)
    {
        var api = new FakeApi { LoseForegroundOnInput = !loseFocus, LoseFocusOnInput = loseFocus };
        var broker = new WindowsDesktopBroker(api);
        var exception = await Assert.ThrowsAsync<NativeOperationException>(() => keyPress
            ? broker.KeyPressAsync(Target, "Ctrl+A", KeyboardPolicy)
            : broker.TypeTextAsync(Target, "secret", KeyboardPolicy));
        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal("afterDispatch", exception.Phase);
        Assert.Equal(expectedCount, exception.DispatchedInputCount);
        Assert.Equal(Target, exception.Target);
        Assert.Contains("may already have occurred", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.Equal(1, api.InputCalls);
        Assert.False(DesktopComputerUse.Automation.AutomationExceptionResultMapper.CreateDiagnostic(exception).Retryable);
    }

    [Fact]
    public async Task PartialInputIsReportedAsFailureWithDispatchCount()
    {
        var api = new FakeApi { MouseDispatchCount = 2, KeyboardDispatchCount = 1 };
        var broker = new WindowsDesktopBroker(api);
        var mouse = await Assert.ThrowsAsync<NativeOperationException>(() =>
            broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy));
        var keyboard = await Assert.ThrowsAsync<NativeOperationException>(() =>
            broker.TypeTextAsync(Target, "x", KeyboardPolicy));
        Assert.Equal(NativeFailureCode.InputDispatchFailed, mouse.Code);
        Assert.Equal(2U, mouse.DispatchedInputCount);
        Assert.Equal(NativeFailureCode.InputDispatchFailed, keyboard.Code);
        Assert.Equal(1U, keyboard.DispatchedInputCount);
        AssertStructuredFailure(mouse);
        AssertStructuredFailure(keyboard);
    }

    [Theory]
    [InlineData("Win+R")]
    [InlineData("Alt+Tab")]
    [InlineData("Ctrl+Alt+Delete")]
    [InlineData("Ctrl+Shift+Escape")]
    [InlineData("Ctrl+V")]
    [InlineData("Alt+F4")]
    public void SystemAndClipboardChordsAreNotAllowlistedByDefault(string chord)
    {
        Assert.Throws<NativeOperationException>(() => NativeKeyChords.Parse(chord, false));
    }

    [Fact]
    public void ExplicitSystemPolicyDoesNotPermitTaskSwitching()
    {
        Assert.Equal(new ushort[] { 0x12, 0x73 }, NativeKeyChords.Parse("Alt+F4", true));
        Assert.Throws<NativeOperationException>(() => NativeKeyChords.Parse("Alt+Tab", true));
        Assert.Equal(new ushort[] { 0x11, 0x10, 0x25 }, NativeKeyChords.Parse("Ctrl+Shift+Left", false));
    }

    [Theory]
    [InlineData("Enter", 0x0D)]
    [InlineData("F12", 0x7B)]
    [InlineData("Shift+Tab", 0x09)]
    [InlineData("Ctrl+Right", 0x27)]
    [InlineData("Shift+Home", 0x24)]
    [InlineData("Ctrl+Shift+PageDown", 0x22)]
    [InlineData(" ctrl+s ", 0x53)]
    public void AllowedChordsPreserveExpectedFinalKey(string chord, ushort expected)
    {
        Assert.Equal(expected, NativeKeyChords.Parse(chord, false)[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Shift+Enter")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+Shift+Alt+Left")]
    [InlineData("ThisIsAnExcessivelyLongInvalidChord")]
    public void InvalidChordsAreRejected(string chord) =>
        Assert.Throws<NativeOperationException>(() => NativeKeyChords.Parse(chord, true));

    [Fact]
    public void WindowOwnershipAndStatePredicatesAreFailClosed()
    {
        var geometry = new FakeApi().GetGeometry(Target);
        Assert.True(NativeGeometryMath.IsOwnedWindow(true, 123, 123, true));
        Assert.False(NativeGeometryMath.IsOwnedWindow(false, 123, 123, true));
        Assert.False(NativeGeometryMath.IsOwnedWindow(true, 999, 123, true));
        Assert.False(NativeGeometryMath.IsOwnedWindow(true, 123, 123, false));
        Assert.True(NativeGeometryMath.IsOwnedHit(123, 123, true, false));
        Assert.True(NativeGeometryMath.IsOwnedHit(123, 123, false, true));
        Assert.False(NativeGeometryMath.IsOwnedHit(999, 123, true, true));
        Assert.False(NativeGeometryMath.IsOwnedHit(123, 123, false, false));
        Assert.True(NativeGeometryMath.IsUsableWindow(geometry));
        Assert.False(NativeGeometryMath.IsUsableWindow(geometry with { IsVisible = false }));
        Assert.False(NativeGeometryMath.IsUsableWindow(geometry with { IsMinimized = true }));
        Assert.False(NativeGeometryMath.IsUsableWindow(geometry with { IsCloaked = true }));
        Assert.True(NativeGeometryMath.IsRestoredWindow(geometry, 1));
        foreach (var placement in new uint[] { 2, 6, 7, 11 })
            Assert.False(NativeGeometryMath.IsRestoredWindow(geometry, placement));
        Assert.False(NativeGeometryMath.IsRestoredWindow(geometry with { IsMinimized = true }, 1));
        Assert.False(NativeGeometryMath.IsRestoredWindow(geometry with { WindowBounds = new(0, 0, 0, 1) }, 1));
        Assert.False(NativeGeometryMath.IsRestoredWindow(geometry with { ClientBounds = new(0, 0, 1, 0) }, 1));
        Assert.Equal((2U, 4U), NativeGeometryMath.MouseButtonFlags(NativeMouseButton.Left));
        Assert.Equal((8U, 16U), NativeGeometryMath.MouseButtonFlags(NativeMouseButton.Right));
        Assert.Equal((32U, 64U), NativeGeometryMath.MouseButtonFlags(NativeMouseButton.Middle));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGeometryMath.MouseButtonFlags((NativeMouseButton)100));
    }

    [Fact]
    public void GeometryRejectsDegenerateDimensionsAndOverflow()
    {
        foreach (var desktop in new[]
        {
            new PhysicalScreenRect(0, 0, 1, 10), new PhysicalScreenRect(0, 0, 10, 1),
            new PhysicalScreenRect(0, 0, 65537, 10), new PhysicalScreenRect(0, 0, 10, 65537),
            new PhysicalScreenRect(0, 0, 0, 0)
        })
            Assert.Throws<ArgumentOutOfRangeException>(() => NativeGeometryMath.ToAbsoluteMouse(new(0, 0), desktop));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGeometryMath.ScaleLogicalPixels(1, 0));
        Assert.Throws<OverflowException>(() => NativeGeometryMath.ClientToScreen(new(1, 0), new(int.MaxValue, 0, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGeometryMath.CaptureToScreen(new(-1, 0), new(new(0, 0, 1, 1), 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGeometryMath.CaptureToScreen(new(0, -1), new(new(0, 0, 1, 1), 1, 1)));
    }

    [Fact]
    public void EncoderRejectsInvalidBuffersAndRedactions()
    {
        foreach (var buffer in new[]
        {
            new NativePixelBuffer(0, 1, []), new NativePixelBuffer(1, 0, []),
            new NativePixelBuffer(1, 1, []), new NativePixelBuffer(16_000_001, 1, [])
        })
            Assert.Throws<ArgumentException>(() => CaptureImageEncoder.RedactAndEncodePng(buffer, []));
        foreach (var region in new[]
        {
            new PhysicalScreenRect(0, 0, 0, 1), new PhysicalScreenRect(-1, 0, 1, 1),
            new PhysicalScreenRect(0, -1, 1, 1), new PhysicalScreenRect(0, 0, 2, 1),
            new PhysicalScreenRect(0, 0, 1, 2)
        })
            Assert.Throws<ArgumentException>(() => CaptureImageEncoder.RedactAndEncodePng(new(1, 1, [1, 2, 3, 4]), [region]));
    }

    [Fact]
    public async Task CaptureAuthorizationGenerationAndExpectedGeometryAreCheckedBeforePixels()
    {
        var api = new FakeApi();
        var pixels = new FakePixels();
        var provider = new WindowsWindowCaptureProvider(new(api), pixels);
        foreach (var options in new[]
        {
            Options() with { EnableScreenshots = false },
            Options() with { Generation = new("", "revision", 1) },
            Options() with { Generation = new("session", "", 1) },
            Options() with { Generation = null! },
            Options() with { SensitiveRegions = null! },
            Options() with { SensitiveRegions = [null!] },
            Options() with { ExpectedGeometry = api.GetGeometry(Target) with { Dpi = 192 } }
        })
        {
            var error = await Assert.ThrowsAsync<NativeOperationException>(() => provider.CaptureAsync(Target, options));
            AssertStructuredFailure(error);
        }
        Assert.Equal(0, pixels.Calls);
    }

    [Fact]
    public async Task CaptureTokensExpireAndRegistryEvictsOldestWithoutCrossSessionReuse()
    {
        var api = new FakeApi { Window = new(-1000, 100, 2, 2), Client = new(-1000, 100, 2, 2) };
        var clock = new FakeClock();
        var provider = new WindowsWindowCaptureProvider(new(api), new FakePixels(), clock: clock);
        var first = await provider.CaptureAsync(Target, Options());
        clock.Now = clock.Now.AddMinutes(6);
        Assert.Throws<NativeOperationException>(() => provider.GetCaptureToken(first.Token.CaptureId, Target, Generation));
        var captures = new List<NativeWindowCapture>();
        for (var i = 0; i < 129; i++)
        {
            clock.Now = clock.Now.AddTicks(1);
            captures.Add(await provider.CaptureAsync(Target, Options()));
        }
        Assert.Throws<NativeOperationException>(() => provider.GetCaptureToken(captures[0].Token.CaptureId, Target, Generation));
        var latest = captures[^1];
        Assert.Equal(captures[1].Token, provider.GetCaptureToken(captures[1].Token.CaptureId, Target, Generation));
        provider.InvalidateSession("unrelated-session");
        Assert.Equal(latest.Token, provider.GetCaptureToken(latest.Token.CaptureId, Target, Generation));
        provider.InvalidateSession(Generation.SessionId);
        Assert.Throws<NativeOperationException>(() => provider.GetCaptureToken(latest.Token.CaptureId, Target, Generation));
    }

    [Fact]
    public async Task CaptureAtExactExpirationBoundaryIsNotPrematurelyPruned()
    {
        var api = new FakeApi { Window = new(0, 0, 2, 2), Client = new(0, 0, 2, 2) };
        var clock = new FakeClock();
        var provider = new WindowsWindowCaptureProvider(new(api), new FakePixels(), clock: clock);
        var first = await provider.CaptureAsync(Target, Options());
        clock.Now = clock.Now.AddMinutes(5);
        await provider.CaptureAsync(Target, Options());
        Assert.Equal(first.Token, provider.GetCaptureToken(first.Token.CaptureId, Target, Generation));
    }

    [Fact]
    public async Task CaptureRejectsChangedGeometryInvalidBufferAndMinimizedState()
    {
        var api = new FakeApi();
        var broker = new WindowsDesktopBroker(api);
        var moving = new WindowsWindowCaptureProvider(broker,
            new FakePixels { AfterCapture = () => api.Window = api.Window with { Width = 501 } });
        var moved = await Assert.ThrowsAsync<NativeOperationException>(() => moving.CaptureAsync(Target, Options()));
        Assert.Equal(NativeFailureCode.StaleCapture, moved.Code);
        AssertStructuredFailure(moved);
        var invalid = new WindowsWindowCaptureProvider(broker, new FakePixels { InvalidBuffer = true });
        var encoded = await Assert.ThrowsAsync<NativeOperationException>(() => invalid.CaptureAsync(Target, Options()));
        Assert.Equal(NativeFailureCode.CaptureFailed, encoded.Code);
        AssertStructuredFailure(encoded);
        var wrongDimensions = new WindowsWindowCaptureProvider(broker, new FakePixels { WrongDimensions = true });
        var dimensionError = await Assert.ThrowsAsync<NativeOperationException>(() => wrongDimensions.CaptureAsync(Target, Options()));
        Assert.Equal(NativeFailureCode.StaleCapture, dimensionError.Code);
        AssertStructuredFailure(dimensionError);
        api.Minimized = true;
        var minimized = await Assert.ThrowsAsync<NativeOperationException>(() => invalid.CaptureAsync(Target, Options()));
        Assert.Equal(NativeFailureCode.CaptureFailed, minimized.Code);
        AssertStructuredFailure(minimized);
    }

    [Fact]
    public void CoordinateEdgesAndOverflowRemainExplicit()
    {
        Assert.Throws<OverflowException>(() => NativeGeometryMath.ClientToScreen(new(0, 1), new(0, int.MaxValue, 1, 1)));
        Assert.Throws<OverflowException>(() => NativeGeometryMath.ScreenToClient(new(int.MinValue, 0), new(1, 0, 1, 1)));
        Assert.Throws<OverflowException>(() => NativeGeometryMath.ScreenToClient(new(0, int.MinValue), new(0, 1, 1, 1)));
        Assert.Throws<OverflowException>(() => NativeGeometryMath.ScaleLogicalPixels(int.MaxValue, 192));
        Assert.Throws<OverflowException>(() => NativeGeometryMath.CaptureToScreen(new(1, 0), new(new(int.MaxValue, 0, 100, 100), 2, 2)));
        Assert.Throws<OverflowException>(() => NativeGeometryMath.CaptureToScreen(new(0, 1), new(new(0, int.MaxValue, 100, 100), 2, 2)));
        var transform = new CapturePixelTransform(new(-100, -200, 10, 20), 10, 20);
        Assert.Equal(new PhysicalScreenPoint(-100, -200), NativeGeometryMath.CaptureToScreen(new(0, 0), transform));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGeometryMath.CaptureToScreen(new(0, 20), transform));
        Assert.Equal(new PhysicalScreenRect(0, 0, 10, 20), NativeGeometryMath.MapSensitiveRegion(transform.SourceRect, transform));
    }

    [Fact]
    public void FullAllowlistProducesExactSequencesAndNeverExposesMutableStoredChords()
    {
        var keys = new Dictionary<string, ushort>
        {
            ["Tab"] = 9, ["Enter"] = 13, ["Escape"] = 27, ["Backspace"] = 8,
            ["Delete"] = 46, ["Space"] = 32, ["Home"] = 36, ["End"] = 35,
            ["Left"] = 37, ["Up"] = 38, ["Right"] = 39, ["Down"] = 40,
            ["PageUp"] = 33, ["PageDown"] = 34
        };
        for (var i = 1; i <= 12; i++) keys["F" + i] = (ushort)(111 + i);
        foreach (var pair in keys)
            Assert.Equal(new ushort[] { pair.Value }, NativeKeyChords.Parse(pair.Key, false));
        foreach (var pair in keys.Where(pair => pair.Value is >= 33 and <= 40))
        {
            Assert.Equal(new ushort[] { 17, pair.Value }, NativeKeyChords.Parse("Ctrl+" + pair.Key, false));
            Assert.Equal(new ushort[] { 16, pair.Value }, NativeKeyChords.Parse("Shift+" + pair.Key, false));
            Assert.Equal(new ushort[] { 17, 16, pair.Value }, NativeKeyChords.Parse("Ctrl+Shift+" + pair.Key, false));
        }
        foreach (var key in "AZYFS")
            Assert.Equal(new ushort[] { 17, key }, NativeKeyChords.Parse("Ctrl+" + key, false));
        ((ushort[])NativeKeyChords.Parse("Ctrl+A", false))[0] = 999;
        Assert.Equal(new ushort[] { 17, 65 }, NativeKeyChords.Parse("Ctrl+A", false));
        AssertStructuredFailure(Assert.Throws<NativeOperationException>(() => NativeKeyChords.Parse(null!, false)));
    }

    [Fact]
    public async Task SuccessfulLifecycleFlagsAndDelayedPostconditionsAreObserved()
    {
        var api = new FakeApi { Minimized = true, Foreground = false, RestoreDelayPolls = 2, ActivationDelayPolls = 2 };
        var broker = new WindowsDesktopBroker(api);
        var restored = await broker.RestoreWindowAsync(Target);
        Assert.True(restored.Restored);
        Assert.False(restored.Activated);
        Assert.False(restored.Geometry.IsMinimized);
        var activated = await broker.ActivateWindowAsync(Target);
        Assert.False(activated.Restored);
        Assert.True(activated.Activated);
        Assert.True(activated.Geometry.IsForeground);
        var alreadyActivated = await broker.ActivateWindowAsync(Target, allowRestore: true);
        Assert.False(alreadyActivated.Restored);
        Assert.False(alreadyActivated.Activated);
        api.Minimized = true;
        api.Foreground = false;
        var restoredActivated = await broker.ActivateWindowAsync(Target, allowRestore: true);
        Assert.True(restoredActivated.Restored);
        Assert.True(restoredActivated.Activated);
        api.Minimized = true;
        var unusable = await Assert.ThrowsAsync<NativeOperationException>(() => broker.ActivateWindowAsync(Target));
        Assert.Equal(NativeFailureCode.WindowActivationFailed, unusable.Code);
        AssertStructuredFailure(unusable);
    }

    [Fact]
    public async Task KeyPressAuthorizationPreparationAndNullTextAreFailClosed()
    {
        var api = new FakeApi { Minimized = true, Foreground = false };
        var broker = new WindowsDesktopBroker(api);
        var disabled = await Assert.ThrowsAsync<NativeOperationException>(() => broker.KeyPressAsync(Target, "Ctrl+A", MousePolicy));
        Assert.Equal(NativeFailureCode.RawInputDisabled, disabled.Code);
        var result = await broker.KeyPressAsync(Target, "Ctrl+A", KeyboardPolicy with { AllowRestore = true, AllowActivate = true });
        Assert.True(result.IsForeground);
        Assert.Equal(Target.WindowHandle, result.WindowHandle);
        Assert.Equal(4U, result.DispatchedInputCount);
        foreach (var invalidText in new[] { null, "", "\udc00", "\ud800A" })
        {
            var error = await Assert.ThrowsAsync<NativeOperationException>(() => broker.TypeTextAsync(Target, invalidText!, KeyboardPolicy));
            Assert.Equal(NativeFailureCode.InvalidArgument, error.Code);
            AssertStructuredFailure(error);
        }
        var validBoundary = await broker.TypeTextAsync(Target, "abc", KeyboardPolicy with { MaximumTextLength = 3 });
        Assert.Equal(6U, validBoundary.DispatchedInputCount);
    }

    [Fact]
    public async Task MouseRevalidatesGeometryForegroundAndCancellationImmediatelyBeforeInput()
    {
        var api = new FakeApi { Foreground = false };
        var broker = new WindowsDesktopBroker(api);
        var notForeground = await Assert.ThrowsAsync<NativeOperationException>(() =>
            broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy));
        Assert.Equal(NativeFailureCode.ForegroundRequired, notForeground.Code);
        AssertStructuredFailure(notForeground);
        Assert.Equal(0, api.InputCalls);
        await broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy with { RequireForeground = false });
        api.Foreground = true;
        api.OnGeometryRead = read => { if (read == api.MovementRead) api.Window = api.Window with { Width = 501 }; };
        api.MovementRead = api.GeometryReads + 3;
        var moved = await Assert.ThrowsAsync<NativeOperationException>(() =>
            broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy));
        Assert.Equal(NativeFailureCode.StaleCapture, moved.Code);
        AssertStructuredFailure(moved);
        api.OnGeometryRead = null;
        using var cancellation = new CancellationTokenSource();
        api.OnHitOwnership = cancellation.Cancel;
        var before = api.InputCalls;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy, cancellationToken: cancellation.Token));
        Assert.Equal(before, api.InputCalls);
    }

    [Fact]
    public async Task MousePolicyBoundsAndButtonAuthorizationAreIndependent()
    {
        var api = new FakeApi();
        var broker = new WindowsDesktopBroker(api);
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy with { AllowMouse = false }));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy, NativeMouseButton.Right));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy, (NativeMouseButton)999));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy with { ConstrainTo = (NativeInputBounds)999 }));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy with { AllowedMouseButtons = null! }));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy with { MaximumTextLength = 0 }));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy with { MaximumTextLength = 10001 }));
        await Assert.ThrowsAsync<NativeOperationException>(() => broker.TypeTextAsync(Target, "x", MousePolicy));
        Assert.Equal(0, api.InputCalls);
        var result = await broker.ClickScreenPointAsync(Target, new(-999, 101),
            MousePolicy with { ConstrainTo = NativeInputBounds.Window, AllowedMouseButtons = [NativeMouseButton.Right] }, NativeMouseButton.Right);
        Assert.Equal(new PhysicalScreenPoint(-999, 101), result.ActualPoint);
    }

    [Fact]
    public async Task UnknownSensitiveBoundsAndUnmappedBoundsFailBeforeCapture()
    {
        var api = new FakeApi();
        var pixels = new FakePixels();
        var provider = new WindowsWindowCaptureProvider(new(api), pixels);
        foreach (var options in new[]
        {
            Options() with { SensitiveGeometryComplete = false },
            Options() with { SensitiveRegions = [new(null)] },
            Options() with { SensitiveRegions = [new(new(0, 0, 10, 10))] }
        })
        {
            var error = await Assert.ThrowsAsync<NativeOperationException>(() => provider.CaptureAsync(Target, options));
            Assert.Equal(NativeFailureCode.SensitiveGeometryUnavailable, error.Code);
            AssertStructuredFailure(error);
        }
        Assert.Equal(0, pixels.Calls);
    }

    [Fact]
    public async Task CaptureIsPngRedactedAndTokensBecomeStaleAfterMoveOrSessionChange()
    {
        var api = new FakeApi();
        var pixels = new FakePixels();
        var provider = new WindowsWindowCaptureProvider(new(api), pixels);
        var capture = await provider.CaptureAsync(Target, Options() with { SensitiveRegions = [new(new(-1000, 100, 1, 1))] });
        Assert.Equal("image/png", capture.MimeType);
        Assert.True(capture.OcclusionSafe);
        Assert.Equal(1, capture.RedactedControlCount);
        var decompressed = DecodePng(capture.ImageBytes);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 255, 3, 2, 1, 255 }, decompressed[..9]);
        Assert.Equal(capture.Token, provider.GetCaptureToken(capture.Token.CaptureId, Target, Generation));
        Assert.Throws<NativeOperationException>(() =>
            provider.GetCaptureToken(capture.Token.CaptureId, Target, Generation with { SessionId = "other" }));
        api.Client = api.Client with { X = -799 };
        Assert.Throws<NativeOperationException>(() => provider.GetCaptureToken(capture.Token.CaptureId, Target, Generation));
        provider.InvalidateSession(Generation.SessionId);
        Assert.Throws<NativeOperationException>(() => provider.GetCaptureToken(capture.Token.CaptureId, Target, Generation));
    }

    [Fact]
    public async Task CaptureClickRejectsGeometryChangedDuringRestore()
    {
        var api = new FakeApi();
        var broker = new WindowsDesktopBroker(api);
        var provider = new WindowsWindowCaptureProvider(broker, new FakePixels());
        var capture = await provider.CaptureAsync(Target, Options());
        api.Minimized = true;
        api.MoveOnRestore = true;
        var error = await Assert.ThrowsAsync<NativeOperationException>(() =>
            broker.ClickCapturePointAsync(Target, capture.Token, Generation, new(100, 50),
                MousePolicy with { AllowRestore = true }));
        Assert.Equal(NativeFailureCode.StaleCapture, error.Code);
        AssertStructuredFailure(error);
        Assert.Equal(0, api.InputCalls);
    }

    [Fact]
    public async Task FailedPrintWindowNeverReturnsScreenPixels()
    {
        var provider = new WindowsWindowCaptureProvider(new(new FakeApi()), new FakePixels { Fail = true });
        var error = await Assert.ThrowsAsync<NativeOperationException>(() =>
            provider.CaptureAsync(Target, Options() with { AllowScreenFallback = true }));
        Assert.Equal(NativeFailureCode.UnsafeScreenCapture, error.Code);
        AssertStructuredFailure(error);
        var noFallback = await Assert.ThrowsAsync<NativeOperationException>(() => provider.CaptureAsync(Target, Options()));
        Assert.Equal(NativeFailureCode.CaptureFailed, noFallback.Code);
    }

    [Fact]
    public async Task CaptureTimeoutDiscardsLateImageAndKeepsNativeGateUntilCompletion()
    {
        var api = new FakeApi();
        var broker = new WindowsDesktopBroker(api, TimeSpan.FromMilliseconds(30));
        using var release = new ManualResetEventSlim();
        var pixels = new FakePixels { BlockUntil = release };
        var provider = new WindowsWindowCaptureProvider(broker, pixels, TimeSpan.FromMilliseconds(50));
        try
        {
            var capture = await Assert.ThrowsAsync<NativeOperationException>(() => provider.CaptureAsync(Target, Options()));
            Assert.Equal(NativeFailureCode.CaptureFailed, capture.Code);
            AssertStructuredFailure(capture);
            var input = await Assert.ThrowsAsync<NativeOperationException>(() =>
                broker.ClickScreenPointAsync(Target, new(-900, 150), MousePolicy));
            Assert.Equal("waitForBroker", input.Phase);
            AssertStructuredFailure(input);
            Assert.Equal(0, api.InputCalls);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void RealNativeApiFailsExplicitlyOnLinux()
    {
        if (OperatingSystem.IsWindows()) return;
        var error = Assert.Throws<NativeOperationException>(() => new WindowsDesktopBroker().GetDesktopLayout());
        Assert.Equal(NativeFailureCode.PlatformNotSupported, error.Code);
    }

    private static NativeCaptureOptions Options() => new()
    {
        EnableScreenshots = true, SensitiveGeometryComplete = true, Generation = Generation
    };

    private static void AssertStructuredFailure(NativeOperationException exception)
    {
        Assert.False(string.IsNullOrWhiteSpace(exception.Operation));
        Assert.False(string.IsNullOrWhiteSpace(exception.Phase));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    private static byte[] DecodePng(byte[] image)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, image[..8]);
        var headerLength = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(8, 4));
        Assert.Equal(13, headerLength);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(image, 12, 4));
        Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, image[24..29]);
        AssertPngCrc(image, 8, headerLength);
        var dataStart = 8 + 12 + headerLength;
        var dataLength = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(dataStart, 4));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(image, dataStart + 4, 4));
        AssertPngCrc(image, dataStart, dataLength);
        var endStart = dataStart + dataLength + 12;
        Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(endStart, 4)));
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(image, endStart + 4, 4));
        AssertPngCrc(image, endStart, 0);
        Assert.Equal(image.Length, endStart + 12);
        using var compressed = new MemoryStream(image, dataStart + 8, dataLength);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        var width = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(20, 4));
        Assert.Equal((long)height * (width * 4L + 1), output.Length);
        return output.ToArray();
    }

    private static void AssertPngCrc(byte[] image, int chunkStart, int payloadLength)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in image.AsSpan(chunkStart + 4, payloadLength + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var carry = (crc & 1) != 0;
                crc >>= 1;
                if (carry) crc ^= 0xEDB88320;
            }
        }
        Assert.Equal(~crc, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(chunkStart + 8 + payloadLength, 4)));
    }

    private sealed class FakePixels : INativeWindowPixelSource
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public ManualResetEventSlim? BlockUntil { get; init; }
        public Action? AfterCapture { get; init; }
        public bool InvalidBuffer { get; init; }
        public bool WrongDimensions { get; init; }
        public NativePixelBuffer? TryPrintWindow(NativeWindowTarget target, PhysicalScreenRect source)
        {
            Calls++;
            BlockUntil?.Wait();
            if (Fail) return null;
            var buffer = new byte[source.Width * source.Height * 4];
            for (var i = 0; i < buffer.Length; i += 4)
            {
                buffer[i] = 1; buffer[i + 1] = 2; buffer[i + 2] = 3;
            }
            AfterCapture?.Invoke();
            return new(WrongDimensions ? source.Width - 1 : source.Width, source.Height, InvalidBuffer ? [] : buffer);
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeApi : IWindowsDesktopApi
    {
        public PhysicalScreenRect Window { get; set; } = new(-1000, 100, 500, 400);
        public PhysicalScreenRect Client { get; set; } = new(-990, 130, 480, 360);
        public bool Foreground { get; set; } = true;
        public bool Minimized { get; set; }
        public bool OwnedFocus { get; set; } = true;
        public bool HitBelongsToTarget { get; init; } = true;
        public bool RestoreSucceeds { get; init; } = true;
        public bool ActivationSucceeds { get; set; } = true;
        public bool MoveOnRestore { get; set; }
        public bool LoseForegroundOnInput { get; init; }
        public bool LoseFocusOnInput { get; init; }
        public uint MouseDispatchCount { get; init; } = 3;
        public uint? KeyboardDispatchCount { get; init; }
        public int RestoreDelayPolls { get; init; }
        public int ActivationDelayPolls { get; init; }
        public Action<int>? OnGeometryRead { get; set; }
        public Action? OnHitOwnership { get; set; }
        public int MovementRead { get; set; }
        public int GeometryReads { get; private set; }
        public int RestoreCalls { get; private set; }
        public int ActivationCalls { get; private set; }
        public int InputCalls { get; private set; }
        private PhysicalScreenPoint cursor;
        private bool restorePending, activationPending;
        private int restorePolls, activationPolls;
        public NativeDesktopLayout GetDesktopLayout() => new(new(-1920, -200, 3840, 1280), []);
        public NativeWindowGeometry GetGeometry(NativeWindowTarget target)
        {
            GeometryReads++;
            OnGeometryRead?.Invoke(GeometryReads);
            return new(target, Window, Window, Client, 144, true, Minimized, false, Foreground);
        }
        public bool QueueRestore(NativeWindowTarget target)
        {
            RestoreCalls++;
            if (RestoreSucceeds)
            {
                if (RestoreDelayPolls > 0) { restorePending = true; restorePolls = RestoreDelayPolls; }
                else ApplyRestore();
            }
            return true;
        }
        private void ApplyRestore()
        {
            Minimized = false;
            if (MoveOnRestore)
            {
                Window = Window with { X = -810, Y = 120 };
                Client = Client with { X = -800, Y = 150 };
            }
        }
        public bool TryActivate(NativeWindowTarget target)
        {
            ActivationCalls++;
            if (ActivationSucceeds && ActivationDelayPolls > 0) { activationPending = true; activationPolls = ActivationDelayPolls; }
            else Foreground = ActivationSucceeds;
            return Foreground;
        }
        public bool IsRestored(NativeWindowTarget target)
        {
            if (restorePending && --restorePolls <= 0) { ApplyRestore(); restorePending = false; }
            return !Minimized;
        }
        public bool IsForeground(NativeWindowTarget target)
        {
            if (activationPending && --activationPolls <= 0) { Foreground = true; activationPending = false; }
            return Foreground;
        }
        public NativeWindowTarget? GetForegroundTarget() => Foreground ? Target : new(999, 999);
        public bool HasOwnedKeyboardFocus(NativeWindowTarget target) => OwnedFocus;
        public long WindowAtPoint(PhysicalScreenPoint point) => HitBelongsToTarget ? targetHandle : 999;
        private const long targetHandle = 456;
        public bool IsTargetOrChild(NativeWindowTarget target, long windowHandle)
        {
            OnHitOwnership?.Invoke();
            return windowHandle == Target.WindowHandle;
        }
        public uint SendMouse(PhysicalScreenPoint point, PhysicalScreenRect desktop, NativeMouseButton button)
        {
            InputCalls++; cursor = point; return MouseDispatchCount;
        }
        public PhysicalScreenPoint GetCursorPosition() => cursor;
        public uint SendUnicode(string text)
        {
            InputCalls++;
            if (LoseForegroundOnInput) Foreground = false;
            if (LoseFocusOnInput) OwnedFocus = false;
            return KeyboardDispatchCount ?? (uint)text.Length * 2;
        }
        public uint SendKeys(IReadOnlyList<ushort> keys)
        {
            InputCalls++;
            if (LoseForegroundOnInput) Foreground = false;
            if (LoseFocusOnInput) OwnedFocus = false;
            return (uint)keys.Count * 2;
        }
    }
}
