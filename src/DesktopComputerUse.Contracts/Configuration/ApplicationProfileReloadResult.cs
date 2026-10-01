namespace DesktopComputerUse.Contracts.Configuration;

public sealed record ApplicationProfileReloadFailure(
    string? SourceFile,
    string Message,
    ProfileValidationStatus ValidationStatus = ProfileValidationStatus.Invalid);

public sealed record ApplicationProfileReloadResult(
    bool Succeeded,
    long Generation,
    DateTimeOffset AttemptedAtUtc,
    IReadOnlyList<ApplicationProfileSummary> Profiles,
    IReadOnlyList<ApplicationProfileReloadFailure> Failures);
