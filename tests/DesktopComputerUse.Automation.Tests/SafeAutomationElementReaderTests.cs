using System.Runtime.InteropServices;
using System.Text.Json;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class SafeAutomationElementReaderTests
{
    [Fact]
    public void Property_failure_preserves_other_properties_and_sanitizes_diagnostics()
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic
        {
            Operation = "inspectControls", CandidateId = "node-0013", Depth = 2,
            ProfileId = "test", ProfileRevision = "revision", ProcessId = 42, Hwnd = 123
        });
        var name = reader.Read<string?>("Name",
            () => throw new COMException("secret provider value", unchecked((int)0x80040201)), null);
        var enabled = reader.Read("IsEnabled", () => true, false);
        Assert.Null(name);
        Assert.True(enabled);
        var failure = Assert.Single(reader.Failures);
        Assert.Equal("readProperty", failure.Phase);
        Assert.Equal("Name", failure.Property);
        Assert.Equal("node-0013", failure.CandidateId);
        Assert.Equal(2, failure.Depth);
        Assert.Equal("COMException", failure.ExceptionType);
        Assert.Equal("0x80040201", failure.HResult);
        Assert.Equal(AutomationErrorCode.ElementNotAvailable, failure.Code);
        var serialized = JsonSerializer.Serialize(failure);
        Assert.DoesNotContain("secret", serialized);
        Assert.DoesNotContain("StackTrace", serialized);
    }

    [Fact]
    public void Cancellation_and_programming_errors_are_not_swallowed()
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        Assert.Throws<OperationCanceledException>(() =>
            reader.Read<int>("Name", () => throw new OperationCanceledException(), 0));
        Assert.Throws<InvalidOperationException>(() =>
            reader.Read<int>("Name", () => throw new InvalidOperationException(), 0));
        Assert.Empty(reader.Failures);
    }

    [Fact]
    public void Child_enumeration_failure_preserves_node_and_successful_siblings()
    {
        var result = SafeAutomationTraversal.Capture(
            "root",
            node => node switch
            {
                "root" => ["bad", "good"],
                "bad" => throw new COMException("bad provider"),
                "good" => ["grandchild"],
                _ => []
            }, 4, 20, new AutomationDiagnostic { Operation = "snapshotApplicationSchema" });
        Assert.Equal(["root", "bad", "good", "grandchild"], result.Nodes.Select(node => node.Element));
        Assert.True(result.Partial);
        Assert.False(result.Truncated);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("enumerateChildren", failure.Phase);
        Assert.Equal("node-0002", failure.CandidateId);
        Assert.Equal(1, failure.Depth);
        Assert.Equal("node-0003", result.Nodes[3].ParentCandidateId);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(5, 2, 2)]
    public void Limits_report_truncation_without_reporting_provider_failure(int depth, int limit, int count)
    {
        var result = SafeAutomationTraversal.Capture(
            "root", node => node == "root" ? ["a", "b"] : [], depth, limit,
            new AutomationDiagnostic());
        Assert.Equal(count, result.Nodes.Count);
        Assert.False(result.Partial);
        Assert.True(result.Truncated);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Complete_leaf_at_depth_boundary_is_not_truncated()
    {
        var result = SafeAutomationTraversal.Capture(
            "leaf", _ => Array.Empty<string>(), 0, 1, new AutomationDiagnostic());
        Assert.False(result.Truncated);
        Assert.False(result.Partial);
    }

    [Fact]
    public void Exact_node_capacity_and_multiple_depth_limits_preserve_correct_truncation()
    {
        var leaf = SafeAutomationTraversal.Capture("leaf", _ => Array.Empty<string>(), 1, 1, new AutomationDiagnostic());
        Assert.False(leaf.Truncated);
        var result = SafeAutomationTraversal.Capture("root", element => element == "root" ?
            ["first", "second"] : ["grandchild"], 1, 3, new AutomationDiagnostic());
        Assert.True(result.Truncated);
        Assert.False(result.Nodes[0].Truncated);
        Assert.True(result.Nodes[1].Truncated);
        Assert.True(result.Nodes[2].Truncated);
        Assert.Equal(3, result.Nodes.Count);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Enumeration_failure_and_limit_can_be_reported_independently()
    {
        var result = SafeAutomationTraversal.Capture(
            "root", node => node == "root" ? ["bad", "extra"] :
                throw new COMException("failure"), 5, 2, new AutomationDiagnostic());
        Assert.True(result.Partial);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Nodes.Count);
    }

    [Fact]
    public void Cancelled_traversal_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SafeAutomationTraversal.Capture(
            "root", _ => Array.Empty<string>(), 5, 10, new AutomationDiagnostic(), cancellation.Token));
    }

    [Fact]
    public void Invalid_traversal_limits_fail_before_reading_provider()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SafeAutomationTraversal.Capture(
            "root", _ => throw new InvalidOperationException(), -1, 10, new AutomationDiagnostic()));
        Assert.Throws<ArgumentOutOfRangeException>(() => SafeAutomationTraversal.Capture(
            "root", _ => throw new InvalidOperationException(), 0, 0, new AutomationDiagnostic()));
    }

    [Fact]
    public void Screen_bounds_have_explicit_metadata_and_allow_negative_coordinates()
    {
        var bounds = new RectangleInfo(-1920, -100, 800, 600);
        Assert.Equal("physicalVirtualScreen", bounds.CoordinateSpace);
        Assert.Equal("pixels", bounds.Units);
        Assert.Equal(-1920, bounds.X);
        var serialized = JsonSerializer.Serialize(bounds, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"coordinateSpace\":\"physicalVirtualScreen\"", serialized);
        Assert.Contains("\"units\":\"pixels\"", serialized);
    }
}
