using System.Runtime.InteropServices;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ControlObserverTests
{
    private static ControlObservationSource Source()
        => new(() => "field", () => false, () => "Field", () => "Edit", () => "TextBox",
            () => true, () => false, () => new(-500, -100, 200, 30), () => "value",
            new Dictionary<string, Func<bool>> { ["Value"] = () => true, ["Invoke"] = () => false });

    [Fact]
    public void Observation_captures_every_property_and_supported_pattern()
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        var summary = ControlObserver.Observe(Source(), TestProfile.Create(), reader);
        Assert.Equal("field", summary.AutomationId);
        Assert.Equal("Field", summary.Name);
        Assert.Equal("Edit", summary.ControlType);
        Assert.Equal("TextBox", summary.ClassName);
        Assert.True(summary.IsEnabled);
        Assert.False(summary.IsOffscreen);
        Assert.Equal(new RectangleInfo(-500, -100, 200, 30), summary.Bounds);
        Assert.Equal("value", summary.Value);
        Assert.False(summary.IsValueRedacted);
        Assert.False(summary.IsPassword);
        Assert.Equal("Value", Assert.Single(summary.SupportedPatterns));
        Assert.Empty(summary.Failures);
        Assert.False(summary.Partial);
    }

    [Fact]
    public void Observation_preserves_node_when_independent_properties_and_patterns_fail()
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic { CandidateId = "node-0001" });
        var source = Source() with
        {
            Name = () => throw new COMException(),
            IsEnabled = () => throw new COMException(),
            Bounds = () => throw new COMException(),
            Patterns = new Dictionary<string, Func<bool>>
            {
                ["Value"] = () => throw new COMException(), ["Invoke"] = () => true
            }
        };
        var summary = ControlObserver.Observe(source, TestProfile.Create(), reader);
        Assert.Null(summary.Name);
        Assert.False(summary.IsEnabled);
        Assert.Equal(new RectangleInfo(0, 0, 0, 0), summary.Bounds);
        Assert.Equal("Edit", summary.ControlType);
        Assert.Equal("value", summary.Value);
        Assert.Equal("Invoke", Assert.Single(summary.SupportedPatterns));
        Assert.Equal(["Name", "IsEnabled", "BoundingRectangle", "Patterns.Value"],
            summary.Failures.Select(failure => failure.Property));
        Assert.True(summary.Partial);
    }

    [Fact]
    public void Observation_reports_each_failed_optional_property_with_safe_defaults()
    {
        var source = Source() with
        {
            ControlType = () => throw new COMException(),
            ClassName = () => throw new COMException(),
            IsOffscreen = () => throw new COMException(),
            Value = () => throw new COMException()
        };
        var summary = ControlObserver.Observe(source, TestProfile.Create(),
            new SafeAutomationElementReader(new AutomationDiagnostic()));
        Assert.Equal("Unknown", summary.ControlType);
        Assert.Null(summary.ClassName);
        Assert.True(summary.IsOffscreen);
        Assert.Null(summary.Value);
        Assert.Equal(["ControlType", "ClassName", "IsOffscreen", "Value"],
            summary.Failures.Select(failure => failure.Property));
        Assert.True(summary.Partial);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("configured")]
    [InlineData("failedId")]
    [InlineData("failedPassword")]
    public void Observation_never_reads_protected_values(string scenario)
    {
        var source = Source() with
        {
            AutomationId = () => scenario == "failedId" ? throw new COMException() : "field",
            IsPassword = () => scenario == "failedPassword" ? throw new COMException() : scenario == "password",
            Value = () => throw new InvalidOperationException("Protected value must not be read")
        };
        var profile = TestProfile.Create() with
        {
            SensitiveAutomationIds = scenario == "configured" ? ["FIELD"] : []
        };
        var summary = ControlObserver.Observe(source, profile,
            new SafeAutomationElementReader(new AutomationDiagnostic()));
        Assert.True(summary.IsValueRedacted);
        Assert.Null(summary.Value);
        if (scenario == "failedId")
            Assert.Equal("AutomationId", Assert.Single(summary.Failures).Property);
        if (scenario == "failedPassword")
            Assert.Equal("IsPassword", Assert.Single(summary.Failures).Property);
    }

    [Fact]
    public void Observation_normalizes_empty_values_and_separates_preexisting_failures()
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        reader.Read<string?>("EarlierProperty", () => throw new COMException(), null);
        var source = Source() with { AutomationId = () => "", Name = () => "", ClassName = () => null, Value = () => "" };
        var summary = ControlObserver.Observe(source, TestProfile.Create(), reader);
        Assert.Null(summary.AutomationId);
        Assert.Null(summary.Name);
        Assert.Null(summary.ClassName);
        Assert.Null(summary.Value);
        Assert.Empty(summary.Failures);
        Assert.Single(reader.Failures);
    }

    [Fact]
    public void Inspection_tree_preserves_siblings_and_aggregates_property_and_child_failures()
    {
        var capture = SafeAutomationTraversal.Capture("root",
            element => element == "root" ? ["bad", "good"] :
                element == "bad" ? throw new COMException() : [], 5, 10,
            new AutomationDiagnostic());
        var tree = ControlObserver.BuildTree(capture, node => ControlObserver.Observe(
            Source() with { Name = () => node.Element == "good" ? throw new COMException() : node.Element },
            TestProfile.Create(), node.Reader));
        Assert.Equal("node-0001", tree.CandidateId);
        Assert.Equal(0, tree.Depth);
        Assert.Equal(2, tree.Children.Count);
        Assert.Equal("bad", tree.Children[0].Control.Name);
        Assert.Null(tree.Children[1].Control.Name);
        Assert.True(tree.Partial);
        Assert.False(tree.Truncated);
        Assert.Equal(2, tree.Failures.Count);
        Assert.All(tree.Children, child => Assert.True(child.Partial));
    }

    [Fact]
    public void Inspection_aggregates_limits_without_inventing_failures()
    {
        var capture = SafeAutomationTraversal.Capture("root", _ => ["child"], 0, 10, new AutomationDiagnostic());
        var tree = ControlObserver.BuildTree(capture,
            node => ControlObserver.Observe(Source(), TestProfile.Create(), node.Reader));
        Assert.True(tree.Truncated);
        Assert.False(tree.Partial);
        Assert.Empty(tree.Failures);
        Assert.Empty(tree.Children);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ControlObserver.BuildTree(capture,
            node => ControlObserver.Observe(Source(), TestProfile.Create(), node.Reader), cancellation.Token));
    }

    [Fact]
    public void Inspection_propagates_child_truncation_to_root()
    {
        var capture = SafeAutomationTraversal.Capture("root",
            element => element == "root" ? ["child"] : ["grandchild"], 1, 10, new AutomationDiagnostic());
        var tree = ControlObserver.BuildTree(capture,
            node => ControlObserver.Observe(Source(), TestProfile.Create(), node.Reader));
        var child = Assert.Single(tree.Children);
        Assert.True(tree.Truncated);
        Assert.True(child.Truncated);
        Assert.Equal("node-0002", child.CandidateId);
        Assert.Equal(1, child.Depth);
        Assert.False(tree.Partial);
        Assert.False(child.Partial);
        Assert.Empty(child.Children);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Failed_sensitivity_property_fails_closed(bool failId, bool failPassword)
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        var state = ControlObserver.ReadSensitivity(reader, TestProfile.Create(),
            () => failId ? throw new COMException("id failure") : "field",
            () => failPassword ? throw new COMException("password failure") : false);
        Assert.True(state.IsSensitive);
        Assert.Equal(failPassword, state.IsPassword);
        Assert.Equal((failId ? 1 : 0) + (failPassword ? 1 : 0), reader.Failures.Count);
    }

    [Fact]
    public void Password_and_configured_sensitive_ids_are_redacted()
    {
        var profile = TestProfile.Create() with { SensitiveAutomationIds = ["SecretField"] };
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        Assert.True(ControlObserver.ReadSensitivity(reader, profile, () => "password", () => true).IsSensitive);
        Assert.True(ControlObserver.ReadSensitivity(reader, profile, () => "secretfield", () => false).IsSensitive);
        Assert.False(ControlObserver.ReadSensitivity(reader, profile, () => "ordinary", () => false).IsSensitive);
        Assert.Empty(reader.Failures);
    }
}
