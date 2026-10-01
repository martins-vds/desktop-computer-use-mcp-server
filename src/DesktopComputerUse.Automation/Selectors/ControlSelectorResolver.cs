using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Automation.FlaUi;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DesktopComputerUse.Automation.Selectors;

public sealed class ControlSelectorResolver
{
    public ControlSelector ExpandSemanticSelector(
        ApplicationProfile profile,
        ControlSelector selector)
        => ExpandSemanticSelectors(profile, selector).First();

    public IReadOnlyList<ControlSelector> ExpandSemanticSelectors(
        ApplicationProfile profile,
        ControlSelector selector)
    {
        if (string.IsNullOrWhiteSpace(selector.SemanticKey))
        {
            return [selector];
        }

        if (!profile.EffectiveSemanticTargets.TryGetValue(
                selector.SemanticKey,
                out var target))
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ControlNotFound,
                $"Semantic selector '{selector.SemanticKey}' is not defined by profile '{profile.Id}'.");
        }

        var configured = target.Strategies
            .OrderByDescending(strategy => strategy.Weight)
            .ToArray();
        if (configured.Length == 0)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ControlNotFound,
                $"Semantic target '{selector.SemanticKey}' has no selector strategies.");
        }

        return configured
            .Select(strategy => (ControlSelector)(strategy with
            {
                SemanticKey = null,
                AutomationId = selector.AutomationId ?? strategy.AutomationId,
                Name = selector.Name ?? strategy.Name,
                ControlType = selector.ControlType ?? strategy.ControlType,
                ClassName = selector.ClassName ?? strategy.ClassName,
                Ancestor = selector.Ancestor ?? strategy.Ancestor,
                Index = selector.Index ?? strategy.Index
            }))
            .ToArray();
    }

    public IReadOnlyList<AutomationElement> FindMatches(
        AutomationElement root,
        ControlSelector selector,
        int maxResults,
        int maxAncestorDepth)
    {
        var result = FindMatchesWithDiagnostics(root, selector, maxResults, maxAncestorDepth);
        result.EnsureCompleteForAction();
        return result;
    }

    public ControlSelectorMatchResult FindMatchesWithDiagnostics(
        AutomationElement root,
        ControlSelector selector,
        int maxResults,
        int maxAncestorDepth,
        int maxNodes = 10000,
        CancellationToken cancellationToken = default)
    {
        ValidateSearch(selector, maxResults, maxAncestorDepth, maxNodes);
        var result = FindCandidates(root, element => element.FindAllChildren(),
            (element, reader) => Matches(element, selector, maxAncestorDepth, reader),
            maxResults, maxNodes, cancellationToken);
        return new(ApplyIndex(result.Matches, selector.Index), result.Failures, result.Truncated);
    }

    internal static void ValidateSearch(
        ControlSelector selector, int maxResults, int maxAncestorDepth, int maxNodes)
    {
        EnsureSelector(selector);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAncestorDepth);
        _ = TryParseControlType(selector.ControlType, out _);
        if (selector.Ancestor is not null)
            _ = TryParseControlType(selector.Ancestor.ControlType, out _);
    }

    internal static SelectorCandidateMatchResult<T> FindCandidates<T>(
        T root,
        Func<T, T[]> getChildren,
        Func<T, SafeAutomationElementReader, bool> matchesCandidate,
        int maxResults,
        int maxNodes,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<AutomationDiagnostic>();
        var matches = new List<T>();
        var pending = new Stack<(T Element, int Depth)>();
        pending.Push((root, 0));
        var visited = 0;
        var truncated = false;
        var context = new AutomationDiagnostic { Operation = "resolveSelector" };
        while (CanContinue(pending.Count, matches.Count, maxResults))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (element, depth) = pending.Pop();
            var reader = new SafeAutomationElementReader(context with
            {
                CandidateId = $"node-{++visited:0000}", Depth = depth
            }, failures);
            matches.AddRange(ReadCandidate(element, reader, matchesCandidate));
            if (matches.Count > maxResults)
            {
                truncated = true;
                break;
            }
            var children = reader.ReadChildren(() => getChildren(element));
            truncated |= QueueChildren(pending, children, depth + 1, maxNodes - visited - pending.Count);
        }
        return new(matches.ToArray(), failures, IsTruncated(truncated, pending.Count));
    }

    private static bool CanContinue(int pending, int matches, int maxResults)
        => pending > 0 && matches <= maxResults;

    private static bool IsTruncated(bool truncated, int pending)
        => truncated || pending > 0;

    private static T[] ReadCandidate<T>(
        T element, SafeAutomationElementReader reader, Func<T, SafeAutomationElementReader, bool> matches)
    {
        var before = reader.Failures.Count;
        return matches(element, reader) && reader.Failures.Count == before ? [element] : [];
    }

    private static bool QueueChildren<T>(Stack<(T Element, int Depth)> pending, T[] children, int depth, int capacity)
    {
        foreach (var child in children.Take(capacity).Reverse())
            pending.Push((child, depth));
        return children.Length > capacity;
    }
    private static void EnsureSelector(ControlSelector selector)
    {
        if (selector.IsEmpty)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ControlNotFound,
                "At least one selector field is required.");
        }
    }

    internal static IReadOnlyList<T> ApplyIndex<T>(
        T[] candidates,
        int? index)
        => index is int value ? SelectIndex(candidates, value) : candidates;

    private static IReadOnlyList<T> SelectIndex<T>(T[] candidates, int index)
        => index >= 0 && index < candidates.Length ? [candidates[index]] : [];

    private static bool Matches(
        AutomationElement element,
        ControlSelector selector,
        int maxAncestorDepth,
        SafeAutomationElementReader reader)
    {
        return MatchesWithoutAncestor(element, selector, reader) &&
            (selector.Ancestor is null ||
             HasMatchingAncestor(element, maxAncestorDepth, reader, ancestor => ancestor.Parent,
                 (ancestor, safeReader) => MatchesWithoutAncestor(ancestor, selector.Ancestor, safeReader)));
    }

    internal static bool HasMatchingAncestor<T>(
        T element,
        int remainingDepth,
        SafeAutomationElementReader reader,
        Func<T, T?> getParent,
        Func<T, SafeAutomationElementReader, bool> matches) where T : class
        => EnumerateAncestors(element, remainingDepth, reader, getParent)
            .Any(ancestor => matches(ancestor, reader));

    private static IEnumerable<T> EnumerateAncestors<T>(
        T element, int remainingDepth, SafeAutomationElementReader reader, Func<T, T?> getParent) where T : class
    {
        var ancestor = element;
        while (remainingDepth-- > 0)
        {
            var parent = reader.Read("Parent", () => getParent(ancestor), null);
            if (parent is null)
                yield break;
            ancestor = parent;
            yield return ancestor;
        }
    }

    private static bool MatchesWithoutAncestor(
        AutomationElement element,
        ControlSelector selector,
        SafeAutomationElementReader reader)
    {
        var properties = new Dictionary<string, Func<string?>>
        {
            ["AutomationId"] = () => element.AutomationId,
            ["Name"] = () => element.Name,
            ["ClassName"] = () => element.ClassName,
            ["ControlType"] = () => element.ControlType.ToString()
        };
        return MatchesProperties(selector, reader, property => properties[property]());
    }

    public static bool MatchesProperties(
        ControlSelector selector,
        SafeAutomationElementReader reader,
        Func<string, string?> getProperty)
    {
        var hasType = TryParseControlType(selector.ControlType, out var type);
        return MatchesProperty("AutomationId", () => getProperty("AutomationId"), selector.AutomationId, reader) &&
            MatchesProperty("Name", () => getProperty("Name"), selector.Name, reader) &&
            MatchesProperty("ClassName", () => getProperty("ClassName"), selector.ClassName, reader) &&
            (!hasType || MatchesProperty("ControlType", () => getProperty("ControlType"), type.ToString(), reader));
    }

    private static bool MatchesProperty(
        string property,
        Func<string?> getter,
        string? expected,
        SafeAutomationElementReader reader)
        => string.IsNullOrWhiteSpace(expected) ||
            (reader.TryRead(property, getter, out var actual) &&
             string.Equals(actual, expected, StringComparison.Ordinal));

    private static bool TryParseControlType(string? value, out ControlType controlType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            controlType = ControlType.Unknown;
            return false;
        }

        if (!Enum.TryParse(value, true, out controlType))
        {
            throw new AutomationOperationException(
                AutomationErrorCode.InvalidProfile,
                $"Unknown control type '{value}'.");
        }

        return true;
    }
}
