using DesktopComputerUse.Automation;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ControlSelectorResolverTests
{
    private readonly ControlSelectorResolver _resolver = new();

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
