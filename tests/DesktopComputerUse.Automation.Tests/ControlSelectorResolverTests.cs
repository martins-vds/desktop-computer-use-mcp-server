using DesktopComputerUse.Automation;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ControlSelectorResolverTests
{
    private readonly ControlSelectorResolver _resolver = new();

    [Theory]
    [InlineData("Save", "ButtonClass", "Button", true)]
    [InlineData("Other", "ButtonClass", "Button", false)]
    [InlineData("Save", "Other", "Button", false)]
    [InlineData("Save", "ButtonClass", "Edit", false)]
    public void Matching_checks_all_requested_properties(string name, string className, string type, bool expected)
    {
        var values = new Dictionary<string, string>
        {
            ["Name"] = name, ["ClassName"] = className, ["ControlType"] = type
        };
        Assert.Equal(expected, ControlSelectorResolver.MatchesProperties(
            new ControlSelector { Name = "Save", ClassName = "ButtonClass", ControlType = "button" },
            new SafeAutomationElementReader(new AutomationDiagnostic()), property => values[property]));
    }

    [Fact]
    public void Empty_semantic_target_strategies_produce_control_not_found()
    {
        var profile = TestProfile.Create() with
        {
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["empty"] = new() { Intent = "Empty target", Strategies = [] }
            }
        };
        var exception = Assert.Throws<AutomationOperationException>(() => _resolver.ExpandSemanticSelectors(
            profile, new ControlSelector { SemanticKey = "empty" }));
        Assert.Equal(AutomationErrorCode.ControlNotFound, exception.Code);
        Assert.Contains("no selector strategies", exception.Message);
    }

    [Theory]
    [InlineData("AutomationId")]
    [InlineData("Name")]
    [InlineData("ClassName")]
    [InlineData("ControlType")]
    public void Matching_reports_the_exact_failed_property(string property)
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        var values = new Dictionary<string, string>
        {
            ["AutomationId"] = "SaveButton", ["Name"] = "Save", ["ClassName"] = "ButtonClass", ["ControlType"] = "Button"
        };
        Assert.False(ControlSelectorResolver.MatchesProperties(
            new ControlSelector { AutomationId = "SaveButton", Name = "Save", ClassName = "ButtonClass", ControlType = "Button" },
            reader, requested => requested == property ?
                throw new System.Runtime.InteropServices.COMException() : values[requested]));
        Assert.Equal(property, Assert.Single(reader.Failures).Property);
    }

    [Fact]
    public void Matching_skips_failed_candidate_and_finds_healthy_sibling()
    {
        var capture = SafeAutomationTraversal.Capture(
            "root", node => node == "root" ? ["bad", "good"] : [], 5, 10,
            new AutomationDiagnostic { Operation = "resolveSelector" });
        var matches = capture.Nodes.Where(node => ControlSelectorResolver.MatchesProperties(
            new ControlSelector { AutomationId = "SaveButton" }, node.Reader,
            property => node.Element switch
            {
                "bad" => throw new System.Runtime.InteropServices.COMException("provider failure"),
                "good" => "SaveButton",
                _ => "MainWindow"
            })).ToArray();
        Assert.Equal("good", Assert.Single(matches).Element);
        var failure = Assert.Single(capture.Failures);
        Assert.Equal("AutomationId", failure.Property);
        Assert.Equal("node-0002", failure.CandidateId);
        Assert.True(capture.Partial);
    }

    [Fact]
    public void Matching_does_not_read_unrequested_properties()
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        Assert.True(ControlSelectorResolver.MatchesProperties(
            new ControlSelector { AutomationId = "SaveButton" }, reader,
            property => property == "AutomationId" ? "SaveButton" :
                throw new InvalidOperationException("Unexpected read")));
        Assert.Empty(reader.Failures);
    }

    [Fact]
    public void Matching_preserves_invalid_control_type_validation()
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        var exception = Assert.Throws<AutomationOperationException>(() =>
            ControlSelectorResolver.MatchesProperties(
                new ControlSelector { ControlType = "NotAControlType" }, reader, _ => null));
        Assert.Equal(AutomationErrorCode.InvalidProfile, exception.Code);
        Assert.Empty(reader.Failures);
    }

    [Fact]
    public void ExpandSemanticSelector_returns_explicit_selector_unchanged()
    {
        var selector = new ControlSelector { AutomationId = "SaveButton" };

        var expanded = _resolver.ExpandSemanticSelector(TestProfile.Create(), selector);

        Assert.Same(selector, expanded);
    }

    [Fact]
    public void ExpandSemanticSelector_expands_keys_case_insensitively()
    {
        var configuredAncestor = new ControlSelector { AutomationId = "Form" };
        var profile = TestProfile.Create() with
        {
            SemanticSelectors = new Dictionary<string, ControlSelector>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Save"] = new()
                {
                    AutomationId = "SaveButton",
                    Name = "Save",
                    ControlType = "Button",
                    ClassName = "ButtonClass",
                    Ancestor = configuredAncestor,
                    Index = 2
                }
            }
        };

        var expanded = _resolver.ExpandSemanticSelector(
            profile,
            new ControlSelector { SemanticKey = "SAVE" });

        Assert.Null(expanded.SemanticKey);
        Assert.Equal("SaveButton", expanded.AutomationId);
        Assert.Equal("Save", expanded.Name);
        Assert.Equal("Button", expanded.ControlType);
        Assert.Equal("ButtonClass", expanded.ClassName);
        Assert.Same(configuredAncestor, expanded.Ancestor);
        Assert.Equal(2, expanded.Index);
    }

    [Fact]
    public void ExpandSemanticSelector_applies_every_explicit_override()
    {
        var configuredAncestor = new ControlSelector { AutomationId = "ConfiguredParent" };
        var overrideAncestor = new ControlSelector { AutomationId = "OverrideParent" };
        var profile = TestProfile.Create() with
        {
            SemanticSelectors = new Dictionary<string, ControlSelector>
            {
                ["field"] = new()
                {
                    AutomationId = "ConfiguredId",
                    Name = "Configured name",
                    ControlType = "Edit",
                    ClassName = "ConfiguredClass",
                    Ancestor = configuredAncestor,
                    Index = 1
                }
            }
        };

        var expanded = _resolver.ExpandSemanticSelector(
            profile,
            new ControlSelector
            {
                SemanticKey = "field",
                AutomationId = "OverrideId",
                Name = "Override name",
                ControlType = "Document",
                ClassName = "OverrideClass",
                Ancestor = overrideAncestor,
                Index = 4
            });

        Assert.Equal("OverrideId", expanded.AutomationId);
        Assert.Equal("Override name", expanded.Name);
        Assert.Equal("Document", expanded.ControlType);
        Assert.Equal("OverrideClass", expanded.ClassName);
        Assert.Same(overrideAncestor, expanded.Ancestor);
        Assert.Equal(4, expanded.Index);
    }

    [Fact]
    public void ExpandSemanticSelector_rejects_unknown_keys_with_control_not_found()
    {
        var exception = Assert.Throws<AutomationOperationException>(
            () => _resolver.ExpandSemanticSelector(
                TestProfile.Create(),
                new ControlSelector { SemanticKey = "missing" }));

        Assert.Equal(AutomationErrorCode.ControlNotFound, exception.Code);
        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandSemanticSelectors_returns_v2_strategies_by_weight()
    {
        var profile = TestProfile.Create() with
        {
            SchemaVersion = 2,
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["save"] = new()
                {
                    Intent = "Save record",
                    Strategies =
                    [
                        new SelectorStrategy
                        {
                            Name = "Apply",
                            ControlType = "Button",
                            Weight = 0.5
                        },
                        new SelectorStrategy
                        {
                            AutomationId = "SaveButton",
                            ControlType = "Button",
                            Weight = 1
                        }
                    ]
                }
            }
        };

        var expanded = _resolver.ExpandSemanticSelectors(
            profile,
            new ControlSelector { SemanticKey = "save" });

        Assert.Equal(2, expanded.Count);
        Assert.Equal("SaveButton", expanded[0].AutomationId);
        Assert.Equal("Apply", expanded[1].Name);
    }
}
