using System.Collections;
using DesktopComputerUse.Automation;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Contracts.Automation;
using FlaUI.Core.AutomationElements;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ControlSelectorMatchResultTests
{
    [Fact]
    public void Match_result_preserves_list_contract_and_diagnostics()
    {
        var element = (AutomationElement)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(AutomationElement));
        var failure = new AutomationDiagnostic { CandidateId = "bad" };
        var result = new ControlSelectorMatchResult([element], [failure], true);
        Assert.True(result.Count is 1);
        Assert.Same(element, result[0]);
        Assert.Same(element, Assert.Single(result));
        Assert.Same(element, Assert.Single(((IEnumerable)result).Cast<AutomationElement>()));
        Assert.Same(failure, Assert.Single(result.Failures));
        Assert.True(result.Partial);
        Assert.True(result.Truncated);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Empty_match_result_is_complete_without_failures()
    {
        var result = new ControlSelectorMatchResult([], [], false);
        Assert.True(result.Count is 0);
        Assert.Empty(result);
        Assert.Empty(((IEnumerable)result).Cast<AutomationElement>());
        Assert.False(result.Partial);
        Assert.False(result.Truncated);
        Assert.True(result.IsComplete);
        result.EnsureCompleteForAction();
    }

    [Fact]
    public void Skipped_property_failure_does_not_imply_an_unreadable_subtree()
    {
        var result = new ControlSelectorMatchResult([],
            [new AutomationDiagnostic { Phase = "readProperty", Property = "Name" }], false);
        Assert.True(result.Partial);
        Assert.True(result.IsComplete);
        result.EnsureCompleteForAction();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unreadable_subtree_cannot_report_not_found_or_authorize_visible_match(bool truncated)
    {
        var failure = new AutomationDiagnostic
        {
            Operation = "resolveSelector", Phase = "enumerateChildren", CandidateId = "bad", Depth = 2,
            ExceptionType = "COMException", HResult = "0x80004005", Code = AutomationErrorCode.ProviderFailure
        };
        var result = new ControlSelectorMatchResult([], [failure], truncated);
        Assert.False(result.IsComplete);
        var exception = Assert.Throws<AutomationOperationException>(result.EnsureCompleteForAction);
        Assert.Equal(AutomationErrorCode.IncompleteControlSearch, exception.Code);
        Assert.Contains("uniqueness cannot be established", exception.Message);
        Assert.Equal(failure with { Code = AutomationErrorCode.IncompleteControlSearch }, exception.Diagnostic);
        Assert.Same(failure, Assert.Single(exception.Failures));
        var mapped = AutomationExceptionResultMapper.Map<string>(exception, false);
        Assert.Equal(AutomationErrorCode.IncompleteControlSearch, mapped.Error?.Code);
        Assert.Same(failure, Assert.Single(mapped.Error!.Failures));
    }

    [Fact]
    public void Truncated_search_produces_explicit_limit_diagnostic()
    {
        var element = (AutomationElement)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));
        var result = new ControlSelectorMatchResult([element], [], true);
        var exception = Assert.Throws<AutomationOperationException>(result.EnsureCompleteForAction);
        Assert.Equal(AutomationErrorCode.IncompleteControlSearch, exception.Code);
        Assert.Equal("resolveSelector", exception.Diagnostic?.Operation);
        Assert.Equal("traversalLimit", exception.Diagnostic?.Phase);
        Assert.Empty(exception.Failures);
        Assert.Same(element, Assert.Single(result));
    }
}
