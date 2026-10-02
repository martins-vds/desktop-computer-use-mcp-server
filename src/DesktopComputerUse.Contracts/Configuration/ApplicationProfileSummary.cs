using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Contracts.Configuration;

public sealed record ApplicationProfileSummary(
    string Id,
    string DisplayName,
    string ExecutablePath,
    AutomationBackend Backend,
    bool EnableScreenshots)
{
    public bool PrivacyMode { get; init; } = true;

    public string? SourceFile { get; init; }

    public string? Revision { get; init; }

    public DateTimeOffset? LoadedAtUtc { get; init; }

    public DateTimeOffset? SourceLastWriteTimeUtc { get; init; }

    public bool IsStale { get; init; }

    public long Generation { get; init; }

    public ProfileValidationStatus ValidationStatus { get; init; } = ProfileValidationStatus.Valid;
}
