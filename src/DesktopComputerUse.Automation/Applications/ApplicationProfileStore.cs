using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Applications;

public sealed class ApplicationProfileStore : IReloadableApplicationProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _reloadLock = new();
    private readonly string? _directory;
    private Snapshot _snapshot;
    private ApplicationProfileReloadResult? _lastReloadResult;

    private sealed record Snapshot(
        IReadOnlyDictionary<string, ApplicationProfile> Profiles,
        long Generation);

    public ApplicationProfileStore(IEnumerable<ApplicationProfile> profiles)
        : this(profiles, null)
    {
    }

    private ApplicationProfileStore(IEnumerable<ApplicationProfile> profiles, string? directory)
    {
        _directory = directory;
        _snapshot = new Snapshot(CreateProfiles(profiles, DateTimeOffset.UtcNow), 1);
    }

    private static IReadOnlyDictionary<string, ApplicationProfile> CreateProfiles(
        IEnumerable<ApplicationProfile> profiles,
        DateTimeOffset loadedAt)
    {
        var dictionary = new Dictionary<string, ApplicationProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            ApplicationProfileValidator.Validate(profile);
            var loaded = profile with
            {
                Metadata = CreateMetadata(profile, loadedAt)
            };
            if (!dictionary.TryAdd(profile.Id, loaded))
            {
                throw new ProfileValidationException(
                    $"Duplicate application profile ID '{profile.Id}'.");
            }
        }

        return dictionary;
    }

    private static ApplicationProfileMetadata CreateMetadata(
        ApplicationProfile profile, DateTimeOffset loadedAt)
        => profile.Metadata is { } metadata
            ? metadata with { LoadedAtUtc = loadedAt, Generation = 1 }
            : new ApplicationProfileMetadata(
                null, loadedAt, null,
                Convert.ToHexString(SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(profile, SerializerOptions))),
                1);

    public long Generation => Volatile.Read(ref _snapshot).Generation;

    public ApplicationProfileReloadResult? LastReloadResult => Volatile.Read(ref _lastReloadResult);

    public IReadOnlyList<ApplicationProfileSummary> List()
        => List(Volatile.Read(ref _snapshot));

    private static IReadOnlyList<ApplicationProfileSummary> List(Snapshot snapshot)
        => snapshot.Profiles.Values
            .OrderBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new ApplicationProfileSummary(
                profile.Id,
                profile.DisplayName,
                profile.ExecutablePath,
                profile.Backend,
                profile.EnableScreenshots)
            {
                SourceFile = profile.Metadata?.SourceFile,
                Revision = profile.Metadata?.Revision,
                LoadedAtUtc = profile.Metadata?.LoadedAtUtc,
                SourceLastWriteTimeUtc = profile.Metadata?.SourceLastWriteTimeUtc,
                Generation = snapshot.Generation,
                ValidationStatus = ProfileValidationStatus.Valid,
                IsStale = IsSourceStale(profile.Metadata)
            })
            .ToArray();

    public bool TryGet(string profileId, out ApplicationProfile profile)
        => Volatile.Read(ref _snapshot).Profiles.TryGetValue(profileId, out profile!);

    public bool IsStale(ApplicationProfile profile)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return !snapshot.Profiles.TryGetValue(profile.Id, out var current) ||
            current.Metadata?.Revision != profile.Metadata?.Revision ||
            IsSourceStale(profile.Metadata);
    }

    private static bool IsSourceStale(ApplicationProfileMetadata? metadata)
    {
        if (metadata?.SourceFile is not { } path)
        {
            return false;
        }

        try
        {
            return !File.Exists(path) ||
                new DateTimeOffset(File.GetLastWriteTimeUtc(path)) != metadata.SourceLastWriteTimeUtc ||
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != metadata.Revision;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public ApplicationProfileReloadResult Reload()
    {
        lock (_reloadLock)
        {
            var previous = Volatile.Read(ref _snapshot);
            var attemptedAt = DateTimeOffset.UtcNow;
            var failures = new List<ApplicationProfileReloadFailure>();
            var profiles = new Dictionary<string, ApplicationProfile>(StringComparer.OrdinalIgnoreCase);
            if (_directory is null)
            {
                failures.Add(new(null, "This profile store has no source directory."));
            }
            else
            {
                try
                {
                    if (!Directory.Exists(_directory) && previous.Profiles.Count != 0)
                    {
                        throw new IOException("The loaded profile source directory is unavailable.");
                    }
                    foreach (var path in EnumerateProfileFiles(_directory))
                    {
                        try
                        {
                            var profile = LoadFile(path);
                            profile = profile with
                            {
                                Metadata = profile.Metadata! with
                                {
                                    LoadedAtUtc = attemptedAt,
                                    Generation = previous.Generation + 1
                                }
                            };
                            if (!profiles.TryAdd(profile.Id, profile))
                            {
                                throw new ProfileValidationException(
                                    $"Duplicate application profile ID '{profile.Id}'.");
                            }
                        }
                        catch (Exception exception) when (IsLoadFailure(exception))
                        {
                            failures.Add(new(path, exception.Message));
                        }
                    }
                }
                catch (Exception exception) when (IsLoadFailure(exception))
                {
                    failures.Add(new(_directory, exception.Message));
                }
            }

            var next = failures.Count == 0
                ? new Snapshot(profiles, previous.Generation + 1)
                : previous;
            if (failures.Count == 0)
            {
                Volatile.Write(ref _snapshot, next);
            }
            var result = new ApplicationProfileReloadResult(
                failures.Count == 0, next.Generation, attemptedAt, List(next), failures.ToArray());
            Volatile.Write(ref _lastReloadResult, result);
            return result;
        }
    }

    private static bool IsLoadFailure(Exception exception)
        => exception is ProfileValidationException or JsonException or IOException or
            UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static IEnumerable<string> EnumerateProfileFiles(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.application.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            : [];

    public static ApplicationProfileStore LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var fullDirectory = Path.GetFullPath(directory);
        var profiles = EnumerateProfileFiles(fullDirectory)
            .Select(LoadFile)
            .ToArray();

        return new ApplicationProfileStore(profiles, fullDirectory);
    }

    public static ApplicationProfile LoadFile(string path)
    {
        path = Path.GetFullPath(path);
        var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        var bytes = File.ReadAllBytes(path);
        if (lastWrite != new DateTimeOffset(File.GetLastWriteTimeUtc(path)))
        {
            throw new IOException($"Profile file '{path}' changed while it was being loaded.");
        }
        var profile = JsonSerializer.Deserialize<ApplicationProfile>(bytes, SerializerOptions)
            ?? throw new ProfileValidationException($"Profile file '{path}' is empty.");

        var profileDirectory = Path.GetDirectoryName(path)
            ?? throw new ProfileValidationException($"Profile file '{path}' has no parent directory.");

        if (string.IsNullOrWhiteSpace(profile.ExecutablePath) ||
            profile.SemanticSelectors is null || profile.SemanticTargets is null ||
            profile.SensitiveAutomationIds is null)
        {
            throw new ProfileValidationException($"Profile file '{path}' contains missing or null configuration.");
        }

        var executablePath = ResolvePath(profile.ExecutablePath, profileDirectory);
        var workingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory)
            ? Path.GetDirectoryName(executablePath)
            : ResolvePath(profile.WorkingDirectory, profileDirectory);

        var normalized = profile with
        {
            Metadata = new ApplicationProfileMetadata(
                path, DateTimeOffset.UtcNow, lastWrite,
                Convert.ToHexString(SHA256.HashData(bytes)), 0),
            ExecutablePath = executablePath,
            WorkingDirectory = workingDirectory,
            SemanticSelectors = new Dictionary<string, ControlSelector>(
                profile.SemanticSelectors,
                StringComparer.OrdinalIgnoreCase),
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>(
                profile.SemanticTargets,
                StringComparer.OrdinalIgnoreCase),
            SensitiveAutomationIds = new HashSet<string>(
                profile.SensitiveAutomationIds,
                StringComparer.OrdinalIgnoreCase)
        };

        ApplicationProfileValidator.Validate(normalized);
        return normalized;
    }

    public static void SaveFile(string path, ApplicationProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ApplicationProfileValidator.Validate(profile);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new ProfileValidationException(
                $"Profile output path '{path}' has no parent directory."));

        var options = new JsonSerializerOptions(SerializerOptions)
        {
            WriteIndented = true
        };
        using var stream = File.Create(fullPath);
        JsonSerializer.Serialize(stream, profile, options);
    }

    private static string ResolvePath(string path, string baseDirectory)
        => Path.GetFullPath(
            Path.IsPathFullyQualified(path)
                ? path
                : Path.Combine(baseDirectory, path));
}
