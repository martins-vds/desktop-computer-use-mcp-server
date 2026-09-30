namespace DesktopComputerUse.Contracts.Automation;

public record ControlSelector
{
    public string? SemanticKey { get; init; }

    public string? AutomationId { get; init; }

    public string? Name { get; init; }

    public string? ControlType { get; init; }

    public string? ClassName { get; init; }

    public ControlSelector? Ancestor { get; init; }

    public int? Index { get; init; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(SemanticKey) &&
        string.IsNullOrWhiteSpace(AutomationId) &&
        string.IsNullOrWhiteSpace(Name) &&
        string.IsNullOrWhiteSpace(ControlType) &&
        string.IsNullOrWhiteSpace(ClassName);
}
