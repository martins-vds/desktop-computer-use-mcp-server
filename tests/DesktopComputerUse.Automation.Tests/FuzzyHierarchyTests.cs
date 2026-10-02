using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.Automation.Tests;

public sealed class FuzzyHierarchyTests
{
    private static SelectorStrategy Picker(int comboIndex = 2)
        => new()
        {
            ControlType = "Button", Index = 0,
            Ancestor = new()
            {
                ControlType = "ComboBox", Index = comboIndex,
                Ancestor = new() { Name = "Nominations" }
            }
        };

    private static ControlResolutionResult Resolve(ControlSnapshot[] controls, SelectorStrategy? strategy = null)
        => new FuzzyControlResolver().Resolve(SnapshotFixtures.Application(controls), "picker",
            new SemanticTargetDefinition
            {
                Intent = "Open",
                ExpectedControlTypes = ["Button"],
                Strategies = [strategy ?? Picker()],
                Thresholds = new() { MinimumConfidence = 1, MinimumMargin = 0 }
            });

    private static ControlSnapshot[] Combo(string id, string parent)
        =>
        [
            SnapshotFixtures.Control(id, controlType: "ComboBox", parentCandidateId: parent),
            SnapshotFixtures.Control($"{id}-open", controlType: "Button", name: "Open", parentCandidateId: id)
        ];

    [Fact]
    public void Exact_nested_strategy_uses_third_combo_hierarchy_not_first_open_button()
    {
        var controls = new List<ControlSnapshot>
        {
            SnapshotFixtures.Control("window", controlType: "Window")
        };
        controls.AddRange(Combo("outside", "window"));
        controls.Add(SnapshotFixtures.Control("form", controlType: "Pane", name: "Nominations",
            parentCandidateId: "window"));
        controls.AddRange(Combo("month", "form"));
        controls.AddRange(Combo("pipeline", "form"));
        controls.AddRange(Combo("shipper", "form"));
        // Accessibility children may extend beyond their parent's screen rectangle.
        controls[^1] = controls[^1] with { Bounds = new RectangleInfo(5000, 5000, 200, 30) };
        var result = Resolve(controls.ToArray());
        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("shipper-open", result.SelectedCandidateId);
        Assert.Equal("exact-strategy", Assert.Single(Assert.Single(result.Candidates).Features).Feature);
    }

    [Fact]
    public void Ancestor_index_is_global_across_matching_forms_and_outer_index_is_global_across_selected_scopes()
    {
        var controls = new List<ControlSnapshot>
        {
            SnapshotFixtures.Control("first", controlType: "Pane", name: "Nominations")
        };
        controls.AddRange(Combo("month", "first"));
        controls.Add(SnapshotFixtures.Control("second", controlType: "Pane", name: "Nominations"));
        controls.AddRange(Combo("pipeline", "second"));
        controls.AddRange(Combo("shipper", "second"));
        Assert.Equal("shipper-open", Resolve(controls.ToArray()).SelectedCandidateId);
        var allCombos = Picker() with { Index = 2, Ancestor = Picker().Ancestor! with { Index = null } };
        Assert.Equal("shipper-open", Resolve(controls.ToArray(), allCombos).SelectedCandidateId);
        var secondForm = Picker(1) with
        {
            Ancestor = Picker(1).Ancestor! with { Ancestor = new() { Name = "Nominations", Index = 1 } }
        };
        Assert.Equal("shipper-open", Resolve(controls.ToArray(), secondForm).SelectedCandidateId);
    }

    [Fact]
    public void Failed_ancestor_identity_is_not_counted_and_its_diagnostic_is_retained()
    {
        var failure = new AutomationDiagnostic
        {
            CandidateId = "broken", Property = "ControlType", Phase = "readProperty",
            Code = AutomationErrorCode.PropertyNotSupported
        };
        var controls = new List<ControlSnapshot>
        {
            SnapshotFixtures.Control("form", controlType: "Pane", name: "Nominations")
        };
        var broken = Combo("broken", "form");
        broken[0] = broken[0] with { Failures = [failure] };
        controls.AddRange(broken);
        controls.AddRange(Combo("month", "form"));
        controls.AddRange(Combo("pipeline", "form"));
        controls.AddRange(Combo("shipper", "form"));
        var result = Resolve(controls.ToArray());
        Assert.Equal("shipper-open", result.SelectedCandidateId);
        Assert.Same(failure, Assert.Single(result.Failures));
        var absent = Resolve(controls.ToArray(), Picker(10));
        Assert.NotEqual(ResolutionStatus.Resolved, absent.Status);
        Assert.Null(absent.SelectedCandidateId);
        Assert.Same(failure, Assert.Single(absent.Failures));
    }

    [Fact]
    public void Failed_target_identity_does_not_consume_outer_index()
    {
        var failure = new AutomationDiagnostic
        {
            CandidateId = "bad", Property = "Name", Phase = "readProperty",
            Code = AutomationErrorCode.PropertyNotSupported
        };
        var strategy = new SelectorStrategy
        {
            Name = "Open", Index = 0, Ancestor = new() { Name = "Nominations" }
        };
        var result = Resolve(
        [
            SnapshotFixtures.Control("form", controlType: "Pane", name: "Nominations"),
            SnapshotFixtures.Control("bad", controlType: "Button", name: "Open", parentCandidateId: "form")
                with { Failures = [failure] },
            SnapshotFixtures.Control("good", controlType: "Button", name: "Open", parentCandidateId: "form")
        ], strategy);
        Assert.Equal("good", result.SelectedCandidateId);
        Assert.Same(failure, Assert.Single(result.Failures));
    }

    [Fact]
    public void Fuzzy_strategy_evidence_uses_the_same_selected_ancestor_subtree()
    {
        var controls = new List<ControlSnapshot>
        {
            SnapshotFixtures.Control("form", controlType: "Pane", name: "Nominations")
        };
        controls.AddRange(Combo("month", "form"));
        controls.AddRange(Combo("pipeline", "form"));
        controls.AddRange(Combo("shipper", "form"));
        var result = Resolve(controls.ToArray(), Picker() with { Index = 10 });
        Assert.NotEqual(ResolutionStatus.Resolved, result.Status);
        Assert.Equal(1, result.Candidates.Single(candidate => candidate.CandidateId == "shipper-open")
            .Features.Single(feature => feature.Feature == "selector-strategy").Score);
        Assert.All(result.Candidates.Where(candidate => candidate.CandidateId != "shipper-open"),
            candidate => Assert.Equal(0,
                candidate.Features.Single(feature => feature.Feature == "selector-strategy").Score));
    }

    [Theory]
    [InlineData("AutomationId", "ScopeId", true)]
    [InlineData("AutomationId", null, false)]
    [InlineData("AutomationId", "", false)]
    [InlineData("AutomationId", " \t", false)]
    [InlineData("Name", "Picker", true)]
    [InlineData("Name", null, false)]
    [InlineData("Name", "", false)]
    [InlineData("Name", " \t", false)]
    [InlineData("ClassName", "PickerClass", true)]
    [InlineData("ClassName", null, false)]
    [InlineData("ClassName", "", false)]
    [InlineData("ClassName", " \t", false)]
    [InlineData("ControlType", "ComboBox", true)]
    [InlineData("ControlType", null, false)]
    [InlineData("ControlType", "", false)]
    [InlineData("ControlType", " \t", false)]
    public void Nested_ancestor_property_failure_blocks_only_a_requested_nonblank_field(
        string property, string? expected, bool required)
    {
        var failure = new AutomationDiagnostic
        {
            CandidateId = "bad", Property = property, Phase = "readProperty",
            Code = AutomationErrorCode.PropertyNotSupported
        };
        var result = Resolve(IdentityScopes(failure), IdentityStrategy(property, expected));
        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal(required ? "good-open" : "bad-open", result.SelectedCandidateId);
        Assert.Equal("exact-strategy", Assert.Single(Assert.Single(result.Candidates).Features).Feature);
        Assert.Same(failure, Assert.Single(result.Failures));
    }

    [Theory]
    [InlineData("AutomationId", "ScopeId")]
    [InlineData("Name", "Picker")]
    [InlineData("ClassName", "PickerClass")]
    [InlineData("ControlType", "ComboBox")]
    public void Unrelated_ancestor_property_failure_does_not_disqualify_required_identity_evidence(
        string property, string expected)
    {
        var failure = new AutomationDiagnostic
        {
            CandidateId = "bad", Property = "HelpText", Phase = "readProperty",
            Code = AutomationErrorCode.PropertyNotSupported
        };
        var result = Resolve(IdentityScopes(failure), IdentityStrategy(property, expected));
        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("bad-open", result.SelectedCandidateId);
        Assert.Same(failure, Assert.Single(result.Failures));
    }

    private static SelectorStrategy IdentityStrategy(string property, string? expected)
    {
        var ancestor = new ControlSelector
        {
            ControlType = property == "ControlType" ? null : "ComboBox",
            Name = property == "ControlType" ? "Picker" : null,
            Index = 0, Ancestor = new() { Name = "Nominations" }
        };
        ancestor = property switch
        {
            "AutomationId" => ancestor with { AutomationId = expected },
            "Name" => ancestor with { Name = expected },
            "ClassName" => ancestor with { ClassName = expected },
            "ControlType" => ancestor with { ControlType = expected },
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
        return new() { Name = "Open", ControlType = "Button", Index = 0, Ancestor = ancestor };
    }

    private static ControlSnapshot[] IdentityScopes(AutomationDiagnostic failure)
        =>
        [
            SnapshotFixtures.Control("other", controlType: "Pane", name: "Other"),
            SnapshotFixtures.Control("outside", controlType: "ComboBox", name: "Picker",
                automationId: "ScopeId", className: "PickerClass", parentCandidateId: "other"),
            SnapshotFixtures.Control("outside-open", controlType: "Button", name: "Open", parentCandidateId: "outside"),
            SnapshotFixtures.Control("form", controlType: "Pane", name: "Nominations"),
            SnapshotFixtures.Control("bad", controlType: "ComboBox", name: "Picker",
                automationId: "ScopeId", className: "PickerClass", parentCandidateId: "form")
                with { Failures = [failure] },
            SnapshotFixtures.Control("bad-open", controlType: "Button", name: "Open", parentCandidateId: "bad"),
            SnapshotFixtures.Control("good", controlType: "ComboBox", name: "Picker",
                automationId: "ScopeId", className: "PickerClass", parentCandidateId: "form"),
            SnapshotFixtures.Control("good-open", controlType: "Button", name: "Open", parentCandidateId: "good")
        ];

    [Fact]
    public void No_ancestor_exact_strategy_index_zero_selects_first_match_without_fuzzy_fallback()
    {
        var result = Resolve(
        [
            SnapshotFixtures.Control("first", controlType: "Button", name: "Open"),
            SnapshotFixtures.Control("second", controlType: "Button", name: "Open")
        ], new() { Name = "Open", ControlType = "Button", Index = 0 });
        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("first", result.SelectedCandidateId);
        Assert.Equal("exact-strategy", Assert.Single(Assert.Single(result.Candidates).Features).Feature);
    }

    [Fact]
    public void Fuzzy_scope_evidence_traverses_distinct_parents_to_overlapping_selected_ancestors()
    {
        var result = Resolve(
        [
            SnapshotFixtures.Control("outer", controlType: "Pane", name: "Nominations"),
            SnapshotFixtures.Control("outer-wrapper", controlType: "Pane", parentCandidateId: "outer"),
            SnapshotFixtures.Control("outer-open", controlType: "Button", name: "Open", parentCandidateId: "outer-wrapper"),
            SnapshotFixtures.Control("inner", controlType: "Pane", name: "Nominations", parentCandidateId: "outer"),
            SnapshotFixtures.Control("inner-wrapper", controlType: "Pane", parentCandidateId: "inner"),
            SnapshotFixtures.Control("inner-open", controlType: "Button", name: "Open", parentCandidateId: "inner-wrapper"),
            SnapshotFixtures.Control("outside", controlType: "Pane", name: "Other"),
            SnapshotFixtures.Control("outside-wrapper", controlType: "Pane", parentCandidateId: "outside"),
            SnapshotFixtures.Control("outside-open", controlType: "Button", name: "Open", parentCandidateId: "outside-wrapper")
        ], new() { Name = "Open", Index = 10, Ancestor = new() { Name = "Nominations" } });
        Assert.NotEqual(ResolutionStatus.Resolved, result.Status);
        foreach (var candidate in result.Candidates)
        {
            var evidence = candidate.Features.Single(feature => feature.Feature == "selector-strategy");
            Assert.Equal(candidate.CandidateId == "outside-open" ? 0 : 1, evidence.Score);
        }
        Assert.Equal(3, result.Candidates.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_deep_ancestor_selector_is_not_silently_ignored(bool invalidType)
    {
        var strategy = Picker() with
        {
            Ancestor = Picker().Ancestor! with
            {
                Ancestor = invalidType ? new() { ControlType = "NotAControlType" } : new()
            }
        };
        var exception = Assert.Throws<AutomationOperationException>(() => Resolve([], strategy));
        Assert.Equal(invalidType ? AutomationErrorCode.InvalidProfile : AutomationErrorCode.ControlNotFound,
            exception.Code);
    }
}
