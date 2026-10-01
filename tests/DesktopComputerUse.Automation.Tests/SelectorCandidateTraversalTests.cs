using System.Runtime.InteropServices;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class SelectorCandidateTraversalTests
{
    [Fact]
    public void Matching_continues_after_property_and_enumeration_failures()
    {
        var result = ControlSelectorResolver.FindCandidates("root",
            element => element switch
            {
                "root" => ["badProperty", "badChildren", "good"],
                "badChildren" => throw new COMException(),
                _ => []
            },
            (element, reader) => reader.Read("AutomationId",
                () => element == "badProperty" ? throw new COMException() : element, "") == "good",
            5, 100);
        Assert.Equal(["good"], result.Matches);
        Assert.Equal(2, result.Failures.Count);
        Assert.Equal("readProperty", result.Failures[0].Phase);
        Assert.Equal("enumerateChildren", result.Failures[1].Phase);
        Assert.Equal("node-0002", result.Failures[0].CandidateId);
        Assert.Equal("node-0003", result.Failures[1].CandidateId);
        Assert.Equal(1, result.Failures[1].Depth);
        Assert.All(result.Failures, failure => Assert.Equal("resolveSelector", failure.Operation));
        Assert.Equal(result.Failures[0].CorrelationId, result.Failures[1].CorrelationId);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Matching_skips_candidate_even_when_callback_matches_after_a_failed_read()
    {
        var result = ControlSelectorResolver.FindCandidates("bad", _ => Array.Empty<string>(),
            (_, reader) =>
            {
                reader.Read("Name", () => throw new COMException(), "");
                return true;
            }, 5, 100);
        Assert.Empty(result.Matches);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void Matching_preserves_preorder_for_index_compatibility()
    {
        var result = ControlSelectorResolver.FindCandidates("root", element => element switch
        {
            "root" => ["first", "second"], "first" => ["grandchild"], _ => []
        }, (_, _) => true, 10, 100);
        Assert.Equal(["root", "first", "grandchild", "second"], result.Matches);
        Assert.False(result.Truncated);
        Assert.Equal(["grandchild"], ControlSelectorResolver.ApplyIndex(result.Matches, 2));
    }

    [Fact]
    public void Exact_capacity_and_repeated_budget_truncation_are_distinct()
    {
        var leaf = ControlSelectorResolver.FindCandidates("leaf", _ => Array.Empty<string>(), (_, _) => true, 10, 1);
        Assert.False(leaf.Truncated);
        var bounded = ControlSelectorResolver.FindCandidates("root", element => element switch
        {
            "root" => ["first", "second"],
            "first" => ["grandchild", "extra1", "extra2"],
            "grandchild" => ["tooDeep"],
            _ => []
        }, (_, _) => true, 100, 5);
        Assert.Equal(5, bounded.Matches.Length);
        Assert.True(bounded.Truncated);
        Assert.Empty(bounded.Failures);
    }

    [Fact]
    public void Matching_does_not_enumerate_children_after_result_limit()
    {
        var result = ControlSelectorResolver.FindCandidates("root", element => element switch
        {
            "root" => ["first", "second"],
            "first" => throw new COMException("must not enumerate after ambiguity is established"),
            _ => []
        }, (_, _) => true, 1, 100);
        Assert.Equal(["root", "first"], result.Matches);
        Assert.Empty(result.Failures);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Result_limit_is_incomplete_even_when_no_siblings_remain_pending()
    {
        var result = ControlSelectorResolver.FindCandidates("root", element => element switch
        {
            "root" => ["child"], "child" => throw new COMException("hidden children must not be enumerated"), _ => []
        }, (_, _) => true, 1, 100);
        Assert.Equal(["root", "child"], result.Matches);
        Assert.True(result.Truncated);
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData(10, 1, 1)]
    [InlineData(10, 2, 2)]
    [InlineData(1, 100, 2)]
    public void Matching_reports_node_and_result_limits(int results, int nodes, int count)
    {
        var result = ControlSelectorResolver.FindCandidates("root",
            element => element == "root" ? ["first", "second", "third"] : [], (_, _) => true, results, nodes);
        Assert.Equal(count, result.Matches.Length);
        Assert.True(result.Truncated);
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(20)]
    public void Invalid_or_unavailable_index_returns_no_matches(int index)
        => Assert.Empty(ControlSelectorResolver.ApplyIndex(new[] { "first", "second" }, index));

    [Fact]
    public void No_index_preserves_candidates()
    {
        var candidates = new[] { "first", "second" };
        Assert.Same(candidates, ControlSelectorResolver.ApplyIndex(candidates, null));
        Assert.Equal(["first"], ControlSelectorResolver.ApplyIndex(candidates, 0));
        Assert.Equal(["second"], ControlSelectorResolver.ApplyIndex(candidates, 1));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void Ancestor_matching_respects_depth(int depth, bool expected)
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        Assert.Equal(expected, ControlSelectorResolver.HasMatchingAncestor("leaf", depth, reader,
            element => element switch { "leaf" => "parent", "parent" => "grandparent", _ => null },
            (element, _) => element == "grandparent"));
        Assert.Empty(reader.Failures);
    }

    [Fact]
    public void Ancestor_matching_returns_false_for_missing_or_failed_parent()
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic());
        Assert.False(ControlSelectorResolver.HasMatchingAncestor("leaf", 10, reader, _ => null, (_, _) => true));
        Assert.False(ControlSelectorResolver.HasMatchingAncestor("leaf", 10, reader,
            _ => throw new COMException(), (_, _) => true));
        Assert.Equal("Parent", Assert.Single(reader.Failures).Property);
    }

    [Fact]
    public void Validation_rejects_empty_selector_and_invalid_limits_before_provider_access()
    {
        var selector = new ControlSelector { Name = "Save" };
        var empty = Assert.Throws<AutomationOperationException>(() => ControlSelectorResolver.ValidateSearch(new(), 10, 10, 100));
        Assert.Equal("At least one selector field is required.", empty.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlSelectorResolver.ValidateSearch(selector, 0, 10, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlSelectorResolver.ValidateSearch(selector, 10, -1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlSelectorResolver.ValidateSearch(selector, 10, 10, 0));
        ControlSelectorResolver.ValidateSearch(selector with { Ancestor = new() { ControlType = "Window" } }, 10, 10, 100);
        ControlSelectorResolver.ValidateSearch(selector, 10, 0, 1);
        var error = Assert.Throws<AutomationOperationException>(() => ControlSelectorResolver.ValidateSearch(
            selector with { Ancestor = new() { ControlType = "InvalidAncestorType" } }, 10, 10, 100));
        Assert.Equal(AutomationErrorCode.InvalidProfile, error.Code);
        Assert.Contains("InvalidAncestorType", error.Message);
    }

    [Fact]
    public void Cancellation_and_programming_errors_are_not_provider_failures()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ControlSelectorResolver.FindCandidates(
            "root", _ => Array.Empty<string>(), (_, _) => true, 10, 100, cancellation.Token));
        Assert.Throws<InvalidOperationException>(() => ControlSelectorResolver.FindCandidates(
            "root", _ => throw new InvalidOperationException(), (_, _) => true, 10, 100));
    }
}
