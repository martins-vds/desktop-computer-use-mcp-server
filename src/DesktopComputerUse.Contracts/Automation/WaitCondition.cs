namespace DesktopComputerUse.Contracts.Automation;

public enum WaitProperty
{
    Exists,
    Name,
    Value,
    IsEnabled,
    IsOffscreen
}

public enum WaitComparison
{
    Equals,
    NotEquals,
    Contains,
    True,
    False
}

public sealed record WaitCondition(
    WaitProperty Property,
    WaitComparison Comparison,
    string? ExpectedValue = null);
