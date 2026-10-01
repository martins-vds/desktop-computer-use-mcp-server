using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ContractDefaultsTests
{
    [Fact]
    public void Diagnostic_and_partial_results_have_empty_failure_collections_and_real_correlation_ids()
    {
        var diagnostic = new AutomationDiagnostic();
        Assert.Empty(diagnostic.MatchingProcesses);
        Assert.True(Guid.TryParseExact(diagnostic.CorrelationId, "N", out _));
        Assert.Empty(new AutomationError(AutomationErrorCode.AutomationFailure, "Failed").Failures);
        var summary = new ControlSummary(null, null, "Window", null, true, false,
            new RectangleInfo(0, 0, 1, 1), null, false, []);
        Assert.Empty(summary.Failures);
        Assert.False(summary.Partial);
        Assert.Empty(new ControlTreeNode(summary, []).Failures);
        var resolution = new ControlResolutionResult(
            default, "save", "Save", "view", null, null, null, "Not selected", []);
        Assert.Empty(resolution.Failures);
        Assert.False(resolution.Partial);
        Assert.True(resolution.TraversalComplete);
        Assert.True((resolution with { Failures = [diagnostic] }).Partial);
    }

    [Fact]
    public void Native_capture_and_geometry_defaults_cannot_attest_an_unknown_session()
    {
        var options = new NativeCaptureOptions();
        Assert.Equal("", options.Generation.SessionId);
        Assert.Equal("", options.Generation.ProfileRevision);
        Assert.Empty(options.SensitiveRegions);
        Assert.False(options.SensitiveGeometryComplete);
        Assert.False(options.EnableScreenshots);
        var geometry = new NativeWindowGeometry(new(1, 2), new(0, 0, 1, 1),
            null, new(0, 0, 1, 1), 96, true, false, false, true);
        Assert.Empty(geometry.Warnings);
    }

    [Fact]
    public void Snapshot_defaults_are_empty_and_coordinate_spaces_are_explicit()
    {
        var control = new ControlSnapshot { CandidateId = "root", ControlType = "Window" };
        Assert.Empty(control.ChildCandidateIds);
        Assert.Empty(control.SiblingCandidateIds);
        Assert.Empty(control.SupportedPatterns);
        Assert.Empty(control.NearbyLabels);
        Assert.Empty(control.TreePath);
        Assert.Empty(control.Failures);
        Assert.Equal("windowRelative", control.RelativeBounds.CoordinateSpace);
        Assert.Equal("normalized", control.RelativeBounds.Units);
        Assert.False(control.Partial);
        var fingerprint = new ControlFingerprint();
        Assert.Empty(fingerprint.SupportedPatterns);
        Assert.Empty(fingerprint.AncestorTokens);
        Assert.Empty(fingerprint.NameTokens);
        Assert.Empty(fingerprint.NearbyLabelTokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Button")]
    public void Legacy_targets_preserve_selector_identity_and_only_real_control_types(string? controlType)
    {
        var ancestor = new ControlSelector { Name = "Form" };
        var selector = new ControlSelector
        {
            AutomationId = "save-id", Name = "Save", ClassName = "ButtonClass",
            ControlType = controlType, Ancestor = ancestor, Index = 2
        };
        var target = SemanticTargetDefinition.FromLegacy("save-record", selector);
        Assert.Equal("save record", target.Intent);
        Assert.Empty(target.Synonyms);
        Assert.Empty(target.RequiredPatterns);
        Assert.Equal(string.IsNullOrWhiteSpace(controlType) ? [] : new[] { controlType },
            target.ExpectedControlTypes);
        var strategy = Assert.Single(target.Strategies);
        Assert.Equal(selector.AutomationId, strategy.AutomationId);
        Assert.Equal(selector.Name, strategy.Name);
        Assert.Equal(controlType, strategy.ControlType);
        Assert.Equal(selector.ClassName, strategy.ClassName);
        Assert.Equal(ancestor, strategy.Ancestor);
        Assert.Equal(2, strategy.Index);
        Assert.Equal(1, strategy.Weight);
        Assert.Equal(1, target.Thresholds.MinimumConfidence);
        Assert.Equal(1, target.Thresholds.MinimumMargin);
    }

    [Fact]
    public void Physical_bounds_exclude_the_bottom_and_right_edges()
    {
        var rectangle = new PhysicalScreenRect(-10, -20, 20, 30);
        Assert.True(rectangle.Contains(new(-10, -20)));
        Assert.True(rectangle.Contains(new(9, 9)));
        Assert.False(rectangle.Contains(new(10, 0)));
        Assert.False(rectangle.Contains(new(0, 10)));
    }

    [Fact]
    public void Invalid_backend_and_window_errors_are_actionable()
    {
        var profile = TestProfile.Create();
        Assert.Contains("backend", Assert.Throws<ProfileValidationException>(
            () => (profile with { Backend = (AutomationBackend)99 }).ValidateWindowAndBackend()).Message);
        Assert.Contains("main-window", Assert.Throws<ProfileValidationException>(
            () => (profile with { MainWindow = null! }).ValidateWindowAndBackend()).Message);
    }
}
