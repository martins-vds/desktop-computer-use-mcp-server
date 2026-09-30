using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Tests;

internal static class SnapshotFixtures
{
    public static ApplicationSnapshot Application(
        IReadOnlyList<ControlSnapshot> controls,
        string viewKey = "view-main")
        => new(
            "test-app",
            42,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-app.exe")),
            AutomationBackend.Uia3,
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            new WindowSnapshot(
                "window",
                "Test application",
                "MainWindow",
                "Window",
                new RectangleInfo(0, 0, 1000, 800),
                new ViewSignature(
                    viewKey,
                    "fixture-hash",
                    ["test", "application"],
                    controls
                        .Select(control => control.AutomationId)
                        .OfType<string>()
                        .ToArray(),
                    controls
                        .Select(control => control.Name)
                        .OfType<string>()
                        .ToArray()),
                controls,
                true));

    public static ControlSnapshot Control(
        string candidateId,
        string controlType = "Edit",
        string? name = null,
        string? automationId = null,
        string? className = null,
        IReadOnlyList<string>? supportedPatterns = null,
        IReadOnlyList<NearbyLabel>? nearbyLabels = null,
        IReadOnlyList<string>? treePath = null,
        RectangleInfo? relativeBounds = null,
        string? parentCandidateId = null)
        => new()
        {
            CandidateId = candidateId,
            ParentCandidateId = parentCandidateId,
            Name = name,
            AutomationId = automationId,
            ControlType = controlType,
            ClassName = className,
            FrameworkId = "WinForm",
            IsEnabled = true,
            IsKeyboardFocusable = true,
            Bounds = new RectangleInfo(100, 100, 200, 30),
            RelativeBounds = relativeBounds ?? new RectangleInfo(0.1, 0.1, 0.2, 0.04),
            SupportedPatterns = supportedPatterns ?? [],
            NearbyLabels = nearbyLabels ?? [],
            TreePath = treePath ?? ["MainWindow", candidateId]
        };
}
