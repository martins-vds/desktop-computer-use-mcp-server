using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
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
        EnsureSelector(selector);

        var candidates = FindInitialCandidates(root, selector)
            .Where(element => Matches(element, selector, maxAncestorDepth))
            .Take(maxResults + 1)
            .ToArray();

        return ApplyIndex(candidates, selector.Index);
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

    private static IReadOnlyList<AutomationElement> ApplyIndex(
        AutomationElement[] candidates,
        int? index)
        => index is int value
            ? value < candidates.Length ? [candidates[value]] : []
            : candidates;

    private static IEnumerable<AutomationElement> FindInitialCandidates(
        AutomationElement root,
        ControlSelector selector)
    {
        var searches = new (bool Applies, Func<AutomationElement[]> Search)[]
        {
            (
                !string.IsNullOrWhiteSpace(selector.AutomationId),
                () => root.FindAllDescendants(
                    factory => factory.ByAutomationId(selector.AutomationId!))),
            (
                !string.IsNullOrWhiteSpace(selector.Name),
                () => root.FindAllDescendants(
                    factory => factory.ByName(selector.Name!))),
            (
                TryParseControlType(selector.ControlType, out var controlType),
                () => root.FindAllDescendants(
                    factory => factory.ByControlType(controlType))),
            (
                !string.IsNullOrWhiteSpace(selector.ClassName),
                () => root.FindAllDescendants(
                    factory => factory.ByClassName(selector.ClassName!)))
        };
        var search = searches.FirstOrDefault(candidate => candidate.Applies).Search;
        var descendants = search?.Invoke() ?? root.FindAllDescendants();

        if (MatchesWithoutAncestor(root, selector))
        {
            return descendants.Prepend(root);
        }

        return descendants;
    }

    private static bool Matches(
        AutomationElement element,
        ControlSelector selector,
        int maxAncestorDepth)
    {
        return MatchesWithoutAncestor(element, selector) &&
            (selector.Ancestor is null ||
             HasMatchingAncestor(element.Parent, selector.Ancestor, maxAncestorDepth));
    }

    private static bool HasMatchingAncestor(
        AutomationElement? ancestor,
        ControlSelector selector,
        int remainingDepth)
        => EnumerateAncestors(ancestor, remainingDepth)
            .Any(candidate => MatchesWithoutAncestor(candidate, selector));

    private static IEnumerable<AutomationElement> EnumerateAncestors(
        AutomationElement? ancestor,
        int remainingDepth)
    {
        while (ancestor is not null && remainingDepth-- > 0)
        {
            yield return ancestor;
            ancestor = ancestor.Parent;
        }
    }

    private static bool MatchesWithoutAncestor(
        AutomationElement element,
        ControlSelector selector)
        => new[]
        {
            MatchesOptional(element.AutomationId, selector.AutomationId, StringComparison.Ordinal),
            MatchesOptional(element.Name, selector.Name, StringComparison.Ordinal),
            MatchesOptional(element.ClassName, selector.ClassName, StringComparison.Ordinal),
            MatchesControlType(element, selector.ControlType)
        }.All(matches => matches);

    private static bool MatchesOptional(
        string? actual,
        string? expected,
        StringComparison comparison)
        => string.IsNullOrWhiteSpace(expected) ||
            string.Equals(actual, expected, comparison);

    private static bool MatchesControlType(
        AutomationElement element,
        string? expected)
        => !TryParseControlType(expected, out var controlType) ||
            element.ControlType == controlType;

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
