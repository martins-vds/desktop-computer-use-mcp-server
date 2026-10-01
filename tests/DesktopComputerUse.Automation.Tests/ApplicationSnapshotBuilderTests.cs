using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Discovery;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ApplicationSnapshotBuilderTests
{
    [Fact]
    public void Runtime_lookup_skips_unavailable_identity_and_preserves_first_duplicate()
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        ApplicationSnapshotBuilder.AddRuntimeKey(lookup, null, "missing");
        Assert.Empty(lookup);
        ApplicationSnapshotBuilder.AddRuntimeKey(lookup, "42.1", "first");
        ApplicationSnapshotBuilder.AddRuntimeKey(lookup, "42.1", "duplicate");
        ApplicationSnapshotBuilder.AddRuntimeKey(lookup, "42.2", "second");
        Assert.Equal("first", lookup["42.1"]);
        Assert.Equal("second", lookup["42.2"]);
        Assert.Equal(2, lookup.Count);
    }

    [Theory]
    [InlineData(0, 800)]
    [InlineData(1000, 0)]
    [InlineData(-1, 800)]
    public void Degenerate_window_bounds_produce_zero_normalized_bounds(double width, double height)
    {
        var bounds = ApplicationSnapshotBuilder.RelativeBounds(new(10, 20, 30, 40), new(0, 0, width, height));
        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(0, bounds.Width);
        Assert.Equal(0, bounds.Height);
        Assert.Equal("windowRelative", bounds.CoordinateSpace);
        Assert.Equal("normalized", bounds.Units);
    }

    [Fact]
    public void Relative_bounds_preserve_negative_virtual_screen_origins()
    {
        var bounds = ApplicationSnapshotBuilder.RelativeBounds(new(-1900, -80, 200, 30), new(-2000, -100, 1000, 500));
        Assert.Equal(0.1, bounds.X);
        Assert.Equal(0.04, bounds.Y);
        Assert.Equal(0.2, bounds.Width);
        Assert.Equal(0.06, bounds.Height);
        Assert.Equal("windowRelative", bounds.CoordinateSpace);
        Assert.Equal("normalized", bounds.Units);
    }

    [Fact]
    public void Path_uses_identity_then_name_then_type_and_stops_at_missing_parent()
    {
        var root = SnapshotFixtures.Control("root", automationId: "MainWindow");
        var parent = SnapshotFixtures.Control("parent", name: "Form", parentCandidateId: "root");
        var child = SnapshotFixtures.Control("child", controlType: "Button", parentCandidateId: "parent");
        var lookup = new[] { root, parent, child }.ToDictionary(control => control.CandidateId);
        Assert.Equal(["MainWindow", "Form", "Button"], ApplicationSnapshotBuilder.BuildPath(child, lookup));
        Assert.Equal(["Button"], ApplicationSnapshotBuilder.BuildPath(child with { ParentCandidateId = "missing" }, lookup));
    }

    [Fact]
    public void Labels_preserve_explicit_relationship_and_ignore_ineligible_geometric_candidates()
    {
        var parent = SnapshotFixtures.Control("root");
        var label = SnapshotFixtures.Control("label", "Text", name: "Name", parentCandidateId: "root") with
        {
            Bounds = new(10, 100, 80, 30)
        };
        var target = SnapshotFixtures.Control("target", parentCandidateId: "root") with
        {
            Bounds = new(100, 100, 200, 30), LabeledByCandidateId = "label"
        };
        var controls = new[]
        {
            parent, label, target,
            label with { CandidateId = "otherParent", ParentCandidateId = "other" },
            label with { CandidateId = "notText", ControlType = "Button" },
            label with { CandidateId = "blank", Name = " " },
            label with { CandidateId = "zeroWidth", Bounds = new(10, 100, 0, 30) },
            label with { CandidateId = "zeroHeight", Bounds = new(100, 80, 80, 0) }
        };
        var labels = ApplicationSnapshotBuilder.FindNearbyLabels(target, controls,
            controls.ToDictionary(control => control.CandidateId));
        var explicitLabel = Assert.Single(labels);
        Assert.Equal("label", explicitLabel.CandidateId);
        Assert.Equal("Name", explicitLabel.Text);
        Assert.Equal("labeledBy", explicitLabel.Relation);
        Assert.Equal(1, explicitLabel.Confidence);
        Assert.Equal(0, explicitLabel.Distance);
    }

    [Fact]
    public void Labels_use_only_valid_sibling_text_when_explicit_label_is_missing()
    {
        var label = SnapshotFixtures.Control("label", "Text", name: "Name", parentCandidateId: "root") with
        {
            Bounds = new(10, 100, 80, 30)
        };
        var target = SnapshotFixtures.Control("target", parentCandidateId: "root") with
        {
            Bounds = new(100, 100, 200, 30), LabeledByCandidateId = "missing"
        };
        var controls = new[] { label, target };
        var lookup = controls.ToDictionary(control => control.CandidateId);
        Assert.Equal("label", Assert.Single(ApplicationSnapshotBuilder.FindNearbyLabels(target, controls, lookup)).CandidateId);
        Assert.Empty(ApplicationSnapshotBuilder.FindNearbyLabels(target with { ParentCandidateId = null }, controls, lookup));
        Assert.Empty(ApplicationSnapshotBuilder.FindNearbyLabels(target with { Bounds = new(100, 100, 0, 30) }, controls, lookup));
        var above = label with { CandidateId = "above", Bounds = new(100, 50, 80, 30) };
        Assert.Empty(ApplicationSnapshotBuilder.FindNearbyLabels(
            target with { Bounds = new(100, 100, 30, 0) }, [above, target],
            new Dictionary<string, ControlSnapshot> { ["above"] = above, ["target"] = target }));
        var blankLabel = label with { Name = "" };
        Assert.Empty(ApplicationSnapshotBuilder.FindNearbyLabels(
            target with { LabeledByCandidateId = "label" }, [blankLabel, target],
            new Dictionary<string, ControlSnapshot> { ["label"] = blankLabel }));
    }

    [Fact]
    public void Enrichment_associates_only_candidate_failures_and_builds_paths()
    {
        var controls = new[]
        {
            SnapshotFixtures.Control("root", automationId: "MainWindow"),
            SnapshotFixtures.Control("child", name: "Child", parentCandidateId: "root")
        };
        var failure = new AutomationDiagnostic { CandidateId = "child", Property = "HelpText" };
        ApplicationSnapshotBuilder.EnrichControls(controls, [failure]);
        Assert.Empty(controls[0].Failures);
        Assert.Same(failure, Assert.Single(controls[1].Failures));
        Assert.Equal(["MainWindow", "Child"], controls[1].TreePath);
        Assert.True(controls[1].Partial);
        Assert.False(controls[0].Partial);
    }

    [Fact]
    public void View_signature_excludes_offscreen_controls_password_names_and_blank_values()
    {
        var window = SnapshotFixtures.Control("window", name: "Main Window", className: "Form");
        var controls = new[]
        {
            window,
            SnapshotFixtures.Control("visible", name: "Zebra", automationId: "Second"),
            SnapshotFixtures.Control("first", name: "Alpha", automationId: "First"),
            SnapshotFixtures.Control("duplicate", name: "Alpha", automationId: "First"),
            SnapshotFixtures.Control("offscreen", name: "Hidden", automationId: "HiddenId") with { IsOffscreen = true },
            SnapshotFixtures.Control("password", name: "Secret", automationId: "PasswordId") with { IsPassword = true },
            SnapshotFixtures.Control("blank", name: " ", automationId: "")
        };
        var signature = ApplicationSnapshotBuilder.BuildViewSignature(window, controls);
        Assert.Equal(["First", "PasswordId", "Second"], signature.VisibleAutomationIds);
        Assert.Equal(["Alpha", "Main Window", "Zebra"], signature.VisibleNames);
        Assert.Equal(64, signature.Hash.Length);
        var expectedText = "Main Window\nForm\nFirst\nPasswordId\nSecond\nAlpha\nMain Window\nZebra";
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(expectedText))).ToLowerInvariant(), signature.Hash);
        Assert.Equal($"view-{signature.Hash[..12]}", signature.Key);
        var reordered = ApplicationSnapshotBuilder.BuildViewSignature(window, controls.Reverse().ToArray());
        Assert.Equal(signature.Hash, reordered.Hash);
        Assert.Equal(signature.TitleTokens, reordered.TitleTokens);
    }

    [Fact]
    public void Geometric_labels_are_ranked_by_confidence_then_distance_and_limited_to_three()
    {
        var target = SnapshotFixtures.Control("target", parentCandidateId: "root") with
        {
            Bounds = new(500, 500, 200, 30)
        };
        var labels = new[]
        {
            SnapshotFixtures.Control("farClamped", "Text", name: "Far", parentCandidateId: "root") with { Bounds = new(-40, 500, 40, 30) },
            SnapshotFixtures.Control("nearClamped", "Text", name: "Near", parentCandidateId: "root") with { Bounds = new(120, 500, 80, 30) },
            SnapshotFixtures.Control("highest", "Text", name: "Highest", parentCandidateId: "root") with { Bounds = new(410, 500, 80, 30) },
            SnapshotFixtures.Control("mid", "Text", name: "Mid", parentCandidateId: "root") with { Bounds = new(300, 500, 80, 30) }
        };
        var controls = labels.Append(target).ToArray();
        var result = ApplicationSnapshotBuilder.FindNearbyLabels(target, controls,
            controls.ToDictionary(control => control.CandidateId));
        Assert.Equal(["highest", "mid", "nearClamped"], result.Select(label => label.CandidateId));
        Assert.True(result[0].Confidence > result[1].Confidence);
        Assert.True(result[1].Confidence > result[2].Confidence);
    }
}
