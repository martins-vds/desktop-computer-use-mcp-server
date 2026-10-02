using System.Runtime.InteropServices;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class HierarchicalSelectorTests
{
    private sealed record Node(string Id, string Type, string? Name = null, params Node[] Children)
    {
        public string? FailedProperty { get; init; }
        public bool Unsupported { get; init; }
        public bool FailChildren { get; init; }
    }

    private static SelectorCandidateMatchResult<Node> Find(
        Node root, ControlSelector selector, int maxResults = 100, int maxDepth = 20,
        int maxNodes = 1000, CancellationToken cancellationToken = default)
        => ControlSelectorResolver.FindSelectorCandidates([root], selector,
            node => node.FailChildren ? throw new COMException() : node.Children,
            (node, stage, reader) => ControlSelectorResolver.MatchesProperties(stage, reader, property =>
            {
                if (node.FailedProperty == property)
                {
                    if (node.Unsupported)
                        throw new COMException("Unsupported property", unchecked((int)0x80040204));
                    throw new COMException();
                }
                return property switch
                {
                    "AutomationId" => node.Id,
                    "ControlType" => node.Type,
                    "Name" => node.Name,
                    "ClassName" => "LegacyControl",
                    _ => throw new InvalidOperationException()
                };
            }), maxResults, maxDepth, maxNodes, cancellationToken);

    private static Node Combo(string id)
        => new(id, "ComboBox", id, new Node($"{id}-open", "Button", "Open"));

    private static ControlSelector Picker(int comboIndex = 2)
        => new()
        {
            ControlType = "Button", Index = 0,
            Ancestor = new()
            {
                ControlType = "ComboBox", Index = comboIndex,
                Ancestor = new() { Name = "Nominations" }
            }
        };

    [Fact]
    public void Nominations_third_combo_scopes_button_to_shipper_not_month_or_other_view()
    {
        var root = new Node("window", "Window", null,
            Combo("outside"),
            new Node("nominations", "Pane", "Nominations",
                Combo("month"), Combo("pipeline"), Combo("shipper")),
            Combo("after"));
        var result = Find(root, Picker());
        Assert.Equal("shipper-open", Assert.Single(result.Matches).Id);
        Assert.Empty(result.Failures);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Indexed_ancestor_is_global_across_scoped_traversal_not_per_parent()
    {
        var root = new Node("window", "Window", null,
            new Node("first", "Pane", "Nominations", Combo("month")),
            new Node("other", "Pane", "Other", Combo("ignored")),
            new Node("second", "Pane", "Nominations", Combo("pipeline"), Combo("shipper")));
        Assert.Equal("shipper-open", Assert.Single(Find(root, Picker()).Matches).Id);
        var allCombos = Picker() with { Index = 2, Ancestor = Picker().Ancestor! with { Index = null } };
        Assert.Equal("shipper-open", Assert.Single(Find(root, allCombos).Matches).Id);
        var secondViewOnly = Picker(1) with
        {
            Ancestor = Picker(1).Ancestor! with { Ancestor = new() { Name = "Nominations", Index = 1 } }
        };
        Assert.Equal("shipper-open", Assert.Single(Find(root, secondViewOnly).Matches).Id);
    }

    [Fact]
    public void Overlapping_scopes_do_not_duplicate_or_reset_indices_and_self_is_not_descendant()
    {
        var root = new Node("window", "Window", null,
            new Node("outer", "Pane", "Nominations",
                new Node("inner", "Pane", "Nominations", Combo("one"), Combo("two"), Combo("three"))));
        Assert.Equal("three-open", Assert.Single(Find(root, Picker()).Matches).Id);
        Assert.Empty(Find(new Node("self", "Button", "Same"),
            new() { Name = "Same", Ancestor = new() { Name = "Same" } }).Matches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_siblings_are_skipped_before_healthy_target_and_do_not_consume_index(bool unsupported)
    {
        var bad = new Node("broken", "Hyperlink", "Schedule")
        {
            FailedProperty = "Name", Unsupported = unsupported
        };
        var root = new Node("window", "Window", null, bad,
            new Node("schedule", "Hyperlink", "Schedule"));
        var result = Find(root, new() { Name = "Schedule", ControlType = "Hyperlink", Index = 0 });
        Assert.Equal("schedule", Assert.Single(result.Matches).Id);
        var diagnostic = Assert.Single(result.Failures);
        Assert.Equal("node-0002", diagnostic.CandidateId);
        Assert.Equal("Name", diagnostic.Property);
        Assert.Equal("readProperty", diagnostic.Phase);
        Assert.Equal(unsupported ? AutomationErrorCode.PropertyNotSupported : AutomationErrorCode.ProviderFailure,
            diagnostic.Code);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Ancestor_failures_survive_when_no_target_matches_and_healthy_ancestor_is_still_usable()
    {
        var root = new Node("window", "Window", null,
            new Node("broken", "Pane", "Nominations") { FailedProperty = "Name", Unsupported = true },
            new Node("good", "Pane", "Nominations", Combo("month"), Combo("pipeline"), Combo("shipper")));
        var matched = Find(root, Picker());
        Assert.Equal("shipper-open", Assert.Single(matched.Matches).Id);
        Assert.Single(matched.Failures);
        var unmatched = Find(root, Picker(10));
        Assert.Empty(unmatched.Matches);
        Assert.Single(unmatched.Failures);
        Assert.False(unmatched.Truncated);
    }

    [Fact]
    public void Failed_intermediate_ancestor_does_not_consume_index()
    {
        var root = new Node("window", "Window", null,
            new Node("form", "Pane", "Nominations",
                Combo("broken") with { FailedProperty = "ControlType" },
                Combo("month"), Combo("pipeline"), Combo("shipper")));
        var result = Find(root, Picker());
        Assert.Equal("shipper-open", Assert.Single(result.Matches).Id);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void Missing_requested_property_is_not_a_match_and_unrequested_property_is_not_read()
    {
        var root = new Node("window", "Window", null,
            new Node("nameless", "Button"),
            new Node("good", "Button", "Save") { FailedProperty = "AutomationId" });
        var result = Find(root, new() { Name = "Save", ControlType = "Button" });
        Assert.Equal("good", Assert.Single(result.Matches).Id);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void No_ancestor_preserves_root_inclusion_and_preorder_indexing()
    {
        var root = new Node("root", "Button", null,
            new Node("first", "Button", null, new Node("grandchild", "Button")),
            new Node("second", "Button"));
        Assert.Equal(["root", "first", "grandchild", "second"],
            Find(root, new() { ControlType = "Button" }).Matches.Select(node => node.Id));
        Assert.Equal("grandchild",
            Assert.Single(Find(root, new() { ControlType = "Button", Index = 2 }).Matches).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deep_empty_or_invalid_selector_is_rejected_before_provider_reads(bool invalidType)
    {
        var root = new Node("window", "Window") { FailChildren = true };
        var selector = Picker() with
        {
            Ancestor = Picker().Ancestor! with
            {
                Ancestor = invalidType ? new() { ControlType = "NotAControlType" } : new()
            }
        };
        var error = Assert.Throws<AutomationOperationException>(() => Find(root, selector));
        Assert.Equal(invalidType ? AutomationErrorCode.InvalidProfile : AutomationErrorCode.ControlNotFound,
            error.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Invalid_or_unavailable_ancestor_index_never_falls_back_to_another_scope(int index)
    {
        var root = new Node("window", "Window", null,
            new Node("form", "Pane", "Nominations", Combo("month"), Combo("pipeline"), Combo("shipper")));
        Assert.Empty(Find(root, Picker(index)).Matches);
    }

    [Fact]
    public void Child_enumeration_failures_and_node_limits_remain_visible()
    {
        var root = new Node("window", "Window", null,
            new Node("broken", "Pane", "Nominations") { FailChildren = true },
            new Node("good", "Pane", "Nominations", Combo("month"), Combo("pipeline"), Combo("shipper")));
        var result = Find(root, Picker());
        Assert.Equal("shipper-open", Assert.Single(result.Matches).Id);
        Assert.Equal("enumerateChildren", Assert.Single(result.Failures).Phase);
        var bounded = Find(root, Picker(), maxNodes: 3);
        Assert.True(bounded.Truncated);
        Assert.Empty(bounded.Matches);
        Assert.Single(bounded.Failures);
    }

    [Fact]
    public void Result_limit_does_not_enumerate_children_after_ambiguity_is_established()
    {
        var root = new Node("window", "Window", null,
            new Node("form", "Pane", "Nominations",
                new Node("first", "Button", "Open"),
                new Node("second", "Button", "Open") { FailChildren = true }));
        var result = Find(root, new() { Name = "Open", Ancestor = new() { Name = "Nominations" } },
            maxResults: 1);
        Assert.Equal(2, result.Matches.Length);
        Assert.True(result.Truncated);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Ancestor_distance_and_cancellation_remain_bounded()
    {
        var root = new Node("form", "Pane", "Nominations",
            new Node("wrapper", "Pane", null, Combo("shipper")));
        Assert.Empty(Find(root, Picker(0), maxDepth: 1).Matches);
        Assert.Equal("shipper-open", Assert.Single(Find(root, Picker(0), maxDepth: 2).Matches).Id);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Find(root, Picker(), cancellationToken: cancellation.Token));
    }
}
