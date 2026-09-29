using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Contracts.Configuration;

public sealed record ApplicationProfile
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public AutomationBackend Backend { get; init; } = AutomationBackend.Uia3;

    public WindowSelector MainWindow { get; init; } = new();

    public int OperationTimeoutMs { get; init; } = 10_000;

    public int PollIntervalMs { get; init; } = 100;

    public int MaxTreeDepth { get; init; } = 5;

    public int MaxResults { get; init; } = 200;

    public bool EnableScreenshots { get; init; }

    public IReadOnlyDictionary<string, ControlSelector> SemanticSelectors { get; init; }
        = new Dictionary<string, ControlSelector>(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> SensitiveAutomationIds { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}
