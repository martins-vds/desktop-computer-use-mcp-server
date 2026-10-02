using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.Automation.Tests;

public sealed class FuzzyControlResolverDiagnosticsTests
{
    private readonly FuzzyControlResolver _resolver = new();
    private static SemanticTargetDefinition Target() => new()
    {
        Intent = "Save", ExpectedControlTypes = ["Edit"],
        Thresholds = new() { MinimumConfidence = 0, MinimumMargin = 0 }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Incomplete_snapshot_keeps_discovery_candidate_without_claiming_unique_resolution(bool truncated)
    {
        var snapshot = SnapshotFixtures.Application([SnapshotFixtures.Control("save", automationId: "SaveButton")]);
        snapshot = snapshot with
        {
            Window = snapshot.Window with
            {
                Truncated = truncated, IsComplete = false, Partial = !truncated,
                Failures = truncated ? [] : [new AutomationDiagnostic { Phase = "enumerateChildren", CandidateId = "hidden" }]
            }
        };
        var result = _resolver.Resolve(snapshot, "save", Target() with { Strategies = [new() { AutomationId = "SaveButton" }] });
        Assert.False(result.TraversalComplete);
        Assert.Equal(ResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.SelectedCandidateId);
        Assert.Equal("save", Assert.Single(result.Candidates).CandidateId);
        Assert.Equal("The snapshot traversal was incomplete; unseen controls may change resolution. Retry inspection before acting.", result.Reason);
    }

    [Fact]
    public void Incomplete_empty_snapshot_is_not_definitive_not_found()
    {
        var snapshot = SnapshotFixtures.Application([]);
        snapshot = snapshot with { Window = snapshot.Window with { Truncated = true, IsComplete = false } };
        var result = _resolver.Resolve(snapshot, "save", Target());
        Assert.Equal(ResolutionStatus.Ambiguous, result.Status);
        Assert.False(result.TraversalComplete);
        Assert.Null(result.SelectedCandidateId);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Incomplete_snapshot_preserves_invalid_target_and_existing_ambiguity_reasons()
    {
        var snapshot = SnapshotFixtures.Application([SnapshotFixtures.Control("save", name: "Save")]);
        snapshot = snapshot with { Window = snapshot.Window with { Truncated = true, IsComplete = false } };
        var invalid = _resolver.Resolve(snapshot, "save", Target() with { Scope = new() { ViewKey = "otherView" } });
        Assert.Equal(ResolutionStatus.InvalidTarget, invalid.Status);
        Assert.False(invalid.TraversalComplete);
        Assert.Equal("The current application view does not match the target scope.", invalid.Reason);
        var ambiguous = _resolver.Resolve(snapshot, "save", Target() with { Thresholds = new() });
        Assert.Equal(ResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.False(ambiguous.TraversalComplete);
        Assert.Equal("Candidate evidence is insufficient or too close to another candidate.", ambiguous.Reason);
    }

    [Theory]
    [InlineData("AutomationId")]
    [InlineData("Name")]
    [InlineData("ClassName")]
    [InlineData("ControlType")]
    [InlineData("IsEnabled")]
    [InlineData("IsOffscreen")]
    [InlineData("IsPassword")]
    [InlineData("Patterns.Value")]
    public void Failed_identity_safety_or_required_pattern_skips_only_bad_candidate(string property)
    {
        var failure = new AutomationDiagnostic { CandidateId = "bad", Property = property };
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("bad", automationId: "SaveButton", supportedPatterns: ["Value"]) with { Failures = [failure] },
            SnapshotFixtures.Control("good", automationId: "SaveButton", supportedPatterns: ["Value"])
        ]);
        var result = _resolver.Resolve(snapshot, "save", Target() with
        {
            RequiredPatterns = ["Value"], Strategies = [new() { AutomationId = "SaveButton" }]
        });
        Assert.Equal("good", result.SelectedCandidateId);
        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Same(failure, Assert.Single(result.Failures));
        Assert.True(result.Partial);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Password_detection_failure_only_blocks_candidates_when_snapshot_privacy_is_enabled(bool privacyMode)
    {
        var failure = new AutomationDiagnostic
        {
            CandidateId = "save", Property = "IsPassword", Phase = "readProperty",
            Code = AutomationErrorCode.PropertyNotSupported
        };
        var snapshot = SnapshotFixtures.Application(
            [SnapshotFixtures.Control("save", automationId: "SaveButton") with { Failures = [failure] }])
            with { PrivacyMode = privacyMode };
        var result = _resolver.Resolve(snapshot, "save", Target() with
        {
            Strategies = [new() { AutomationId = "SaveButton" }]
        });
        Assert.Equal(privacyMode ? ResolutionStatus.NotFound : ResolutionStatus.Resolved, result.Status);
        Assert.Equal(privacyMode ? null : "save", result.SelectedCandidateId);
        Assert.Same(failure, Assert.Single(result.Failures));
    }

    [Fact]
    public void Privacy_disabled_password_failure_does_not_block_fuzzy_or_nested_exact_resolution()
    {
        var failure = new AutomationDiagnostic
        {
            CandidateId = "save", Property = "IsPassword", Phase = "readProperty",
            Code = AutomationErrorCode.PropertyNotSupported
        };
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("form", controlType: "Pane", name: "Form"),
            SnapshotFixtures.Control("save", name: "Save", automationId: "SaveButton", parentCandidateId: "form")
                with { Failures = [failure] }
        ]) with { PrivacyMode = false };
        var scored = _resolver.Resolve(snapshot, "save", Target());
        Assert.Equal(ResolutionStatus.Resolved, scored.Status);
        Assert.Equal("save", scored.SelectedCandidateId);
        var nested = _resolver.Resolve(snapshot, "save", Target() with
        {
            Strategies = [new() { AutomationId = "SaveButton", Ancestor = new() { Name = "Form" } }]
        });
        Assert.Equal(ResolutionStatus.Resolved, nested.Status);
        Assert.Equal("save", nested.SelectedCandidateId);
        Assert.Same(failure, Assert.Single(nested.Failures));
    }

    [Theory]
    [InlineData("AutomationId")]
    [InlineData("Name")]
    [InlineData("ClassName")]
    [InlineData("ControlType")]
    [InlineData("IsEnabled")]
    [InlineData("IsOffscreen")]
    [InlineData("Patterns.Value")]
    public void Privacy_disabled_does_not_waive_identity_safety_or_required_pattern_failures(string property)
    {
        var failure = new AutomationDiagnostic { CandidateId = "save", Property = property };
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("save", name: "Save", automationId: "SaveButton", className: "TextBox",
                supportedPatterns: ["Value"]) with { Failures = [failure] }
        ]) with { PrivacyMode = false };
        var result = _resolver.Resolve(snapshot, "save", Target() with
        {
            RequiredPatterns = ["Value"],
            Strategies = [new() { AutomationId = "SaveButton", Name = "Save", ClassName = "TextBox", ControlType = "Edit" }]
        });
        Assert.Equal(ResolutionStatus.NotFound, result.Status);
        Assert.Null(result.SelectedCandidateId);
        Assert.Same(failure, Assert.Single(result.Failures));
    }

    [Fact]
    public void Snapshot_and_control_diagnostics_are_deduplicated_without_dropping_candidate()
    {
        var failure = new AutomationDiagnostic { CandidateId = "good", Property = "HelpText" };
        var snapshot = SnapshotFixtures.Application(
            [SnapshotFixtures.Control("good", name: "Save") with { Failures = [failure] }]);
        snapshot = snapshot with { Window = snapshot.Window with { Partial = true, Failures = [failure] } };
        var result = _resolver.Resolve(snapshot, "save", Target());
        Assert.Equal("good", result.SelectedCandidateId);
        Assert.Same(failure, Assert.Single(result.Failures));
    }

    [Fact]
    public void Returned_reasons_and_evidence_explain_exact_fuzzy_and_ambiguous_results()
    {
        var control = SnapshotFixtures.Control("save", name: "Save", automationId: "SaveButton") with { HelpText = "Save record" };
        var snapshot = SnapshotFixtures.Application([control]);
        var exact = _resolver.Resolve(snapshot, "save", Target() with { Strategies = [new() { AutomationId = "SaveButton" }] });
        Assert.Equal("A configured selector strategy matched exactly and uniquely.", exact.Reason);
        Assert.Equal("Configured selector fields matched exactly.", Assert.Single(Assert.Single(exact.Candidates).Features).Evidence);
        var scored = _resolver.Resolve(snapshot, "save", Target() with { Fingerprint = new() });
        Assert.Equal("The best candidate met the configured confidence and margin thresholds.", scored.Reason);
        Assert.All(Assert.Single(scored.Candidates).Features, feature => Assert.False(string.IsNullOrWhiteSpace(feature.Evidence)));
        var ambiguous = _resolver.Resolve(snapshot, "save", Target() with { Thresholds = new() { MinimumConfidence = 1, MinimumMargin = 1 } });
        Assert.Equal(ResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Equal("Candidate evidence is insufficient or too close to another candidate.", ambiguous.Reason);
    }

    [Fact]
    public void Ancestor_label_similarity_boundary_is_inclusive()
    {
        var control = SnapshotFixtures.Control("save", name: "Save", treePath: ["a b c d"]);
        var result = _resolver.Resolve(SnapshotFixtures.Application([control]), "save", Target() with
        {
            Scope = new() { AncestorLabels = ["a b c d e"] }
        });
        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("save", result.SelectedCandidateId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Strategy_scoring_retains_exact_identity_component_when_another_component_mismatches(bool exactId)
    {
        var control = SnapshotFixtures.Control("save", name: "Save", automationId: "SaveButton");
        var result = _resolver.Resolve(SnapshotFixtures.Application([control]), "save", Target() with
        {
            Strategies = [new() { AutomationId = exactId ? "SaveButton" : "OtherId", Name = exactId ? "OtherName" : "Save" }]
        });
        var score = Assert.Single(result.Candidates).Features.Single(feature => feature.Feature == "selector-strategy").Score;
        Assert.Equal(0.5, score);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Type_and_class_mismatches_contribute_zero_to_strategy_score(bool mismatchType)
    {
        var control = SnapshotFixtures.Control("save", name: "Save", className: "TextBox");
        var result = _resolver.Resolve(SnapshotFixtures.Application([control]), "save", Target() with
        {
            Strategies = [new()
            {
                Name = "Save", ControlType = mismatchType ? "Button" : "Edit",
                ClassName = mismatchType ? "TextBox" : "OtherClass"
            }]
        });
        Assert.Equal(2d / 3d,
            Assert.Single(result.Candidates).Features.Single(feature => feature.Feature == "selector-strategy").Score);
    }

    [Fact]
    public void Ancestor_only_strategy_with_multiple_matches_has_zero_field_similarity()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("form", controlType: "Window", name: "Form"),
            SnapshotFixtures.Control("first", name: "Save", parentCandidateId: "form"),
            SnapshotFixtures.Control("second", name: "Save", parentCandidateId: "form")
        ]);
        var result = _resolver.Resolve(snapshot, "save", Target() with
        {
            Strategies = [new() { Ancestor = new() { Name = "Form" } }]
        });
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, candidate =>
            Assert.Equal(0, candidate.Features.Single(feature => feature.Feature == "selector-strategy").Score));
    }

    [Fact]
    public void Ancestor_optional_whitespace_fields_are_ignored()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("form", controlType: "Window", name: "Form", className: "FormClass"),
            SnapshotFixtures.Control("save", name: "Save", automationId: "SaveButton", parentCandidateId: "form")
        ]);
        var result = _resolver.Resolve(snapshot, "save", Target() with
        {
            Strategies = [new()
            {
                AutomationId = "SaveButton",
                Ancestor = new() { Name = "Form", ControlType = " ", ClassName = "\t" }
            }]
        });
        Assert.Equal("save", result.SelectedCandidateId);
        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal(1, result.Score);
        Assert.Equal("exact-strategy", Assert.Single(Assert.Single(result.Candidates).Features).Feature);
    }

    [Theory]
    [InlineData("Button", "FormClass")]
    [InlineData("Window", "OtherClass")]
    public void Ancestor_type_and_class_mismatches_prevent_exact_resolution(string type, string className)
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("form", controlType: "Window", name: "Form", className: "FormClass"),
            SnapshotFixtures.Control("save", name: "Save", automationId: "SaveButton", parentCandidateId: "form")
        ]);
        var result = _resolver.Resolve(snapshot, "save", Target() with
        {
            Thresholds = new(),
            Strategies = [new()
            {
                AutomationId = "SaveButton", Ancestor = new() { Name = "Form", ControlType = type, ClassName = className }
            }]
        });
        Assert.Equal(ResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.SelectedCandidateId);
        Assert.Equal(0, Assert.Single(result.Candidates).Features.Single(feature => feature.Feature == "selector-strategy").Score);
    }
}
