using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Server.Linux;

public sealed class PortableProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _reloadLock = new();
    private readonly string _directory;
    private Snapshot _snapshot;
    private ApplicationProfileReloadResult? _lastReloadResult;

    private sealed record Snapshot(IReadOnlyList<ApplicationProfileSummary> Profiles, long Generation);

    private PortableProfileStore(string directory, IReadOnlyList<ApplicationProfileSummary> profiles)
    {
        _directory = directory;
        _snapshot = new(profiles, 1);
    }

    public long Generation => Volatile.Read(ref _snapshot).Generation;

    public ApplicationProfileReloadResult? LastReloadResult => Volatile.Read(ref _lastReloadResult);

    public IReadOnlyList<ApplicationProfileSummary> List()
        => List(Volatile.Read(ref _snapshot));

    private static IReadOnlyList<ApplicationProfileSummary> List(Snapshot snapshot)
        => snapshot.Profiles.Select(profile => profile with { IsStale = IsStale(profile) }).ToArray();

    private static bool IsStale(ApplicationProfileSummary profile)
    {
        try
        {
            return profile.SourceFile is { } path &&
                (!File.Exists(path) ||
                 new DateTimeOffset(File.GetLastWriteTimeUtc(path)) != profile.SourceLastWriteTimeUtc ||
                 Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != profile.Revision);
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
            var profiles = new Dictionary<string, ApplicationProfileSummary>(StringComparer.OrdinalIgnoreCase);
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
                        var profile = LoadSummary(path, attemptedAt, previous.Generation + 1);
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

            var next = failures.Count == 0
                ? new Snapshot(profiles.Values.OrderBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(), previous.Generation + 1)
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

    public static PortableProfileStore LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var fullDirectory = Path.GetFullPath(directory);
        var loadedAt = DateTimeOffset.UtcNow;
        var profiles = new Dictionary<string, ApplicationProfileSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateProfileFiles(fullDirectory))
        {
            var profile = LoadSummary(path, loadedAt, 1);
            if (!profiles.TryAdd(profile.Id, profile))
            {
                throw new ProfileValidationException($"Duplicate application profile ID '{profile.Id}'.");
            }
        }
        return new PortableProfileStore(fullDirectory, profiles.Values
            .OrderBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static ApplicationProfileSummary LoadSummary(string path, DateTimeOffset loadedAt, long generation)
    {
        var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        var bytes = File.ReadAllBytes(path);
        if (lastWrite != new DateTimeOffset(File.GetLastWriteTimeUtc(path)))
        {
            throw new IOException($"Profile file '{path}' changed while it was being loaded.");
        }
        var profile = JsonSerializer.Deserialize<ApplicationProfile>(bytes, SerializerOptions)
            ?? throw new ProfileValidationException($"Profile file '{path}' is empty.");

        var requiredValues = new[]
        {
            profile.Id,
            profile.DisplayName,
            profile.ExecutablePath
        };
        if (requiredValues.Any(string.IsNullOrWhiteSpace))
        {
            throw new ProfileValidationException(
                $"Profile file '{path}' must define id, displayName, and executablePath.");
        }

        Validate(profile);

        return new ApplicationProfileSummary(
            profile.Id,
            profile.DisplayName,
            profile.ExecutablePath,
            profile.Backend,
            profile.EnableScreenshots)
        {
            SourceFile = path,
            Revision = Convert.ToHexString(SHA256.HashData(bytes)),
            LoadedAtUtc = loadedAt,
            SourceLastWriteTimeUtc = lastWrite,
            Generation = generation
        };
    }

    private static void Validate(ApplicationProfile profile)
    {
        ValidateIdentityAndLimits(profile);
        ValidateCollections(profile);
        profile.NativeInput.Validate();
        foreach (var (key, selector) in profile.SemanticSelectors)
        {
            ValidateSelector(profile.Id, key, selector);
        }
        foreach (var (key, target) in profile.SemanticTargets)
        {
            ValidateTarget(profile.Id, key, target);
        }
    }

    private static void ValidateIdentityAndLimits(ApplicationProfile profile)
    {
        profile.ValidateWindowAndBackend();
        if (profile.Id.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_') ||
            profile.SchemaVersion is < 1 or > 2 ||
            profile.OperationTimeoutMs is < 100 or > 300_000 ||
            profile.PollIntervalMs is < 10 or > 10_000 ||
            profile.MaxTreeDepth is < 1 or > 20 ||
            profile.MaxResults is < 1 or > 5_000)
        {
            throw new ProfileValidationException($"Application profile '{profile.Id}' has invalid configuration.");
        }
    }

    private static void ValidateCollections(ApplicationProfile profile)
    {
        if (profile.NativeInput is null ||
            profile.SemanticSelectors is null || profile.SemanticTargets is null ||
            profile.SensitiveAutomationIds is null)
        {
            throw new ProfileValidationException($"Application profile '{profile.Id}' has invalid configuration.");
        }
    }

    private static void ValidateSelector(string profileId, string key, ControlSelector? selector)
    {
        if (string.IsNullOrWhiteSpace(key) || selector is null || selector.IsEmpty ||
            !string.IsNullOrWhiteSpace(selector.SemanticKey) || selector.Index is < 0)
        {
            throw new ProfileValidationException($"Application profile '{profileId}' has an invalid selector.");
        }
        ValidateSelectorControlType(profileId, selector.ControlType);
    }

    private static void ValidateSelectorControlType(string profileId, string? controlType)
    {
        if (!string.IsNullOrWhiteSpace(controlType) && !KnownControlTypes.Contains(controlType))
        {
            throw new ProfileValidationException($"Application profile '{profileId}' has an invalid selector control type.");
        }
    }

    private static void ValidateTarget(string profileId, string key, SemanticTargetDefinition? target)
    {
        if (string.IsNullOrWhiteSpace(key) || target is null ||
            string.IsNullOrWhiteSpace(target.Intent) || target.Strategies is null ||
            target.Strategies.Count == 0)
        {
            throw new ProfileValidationException($"Application profile '{profileId}' has an invalid semantic target.");
        }
        ValidateThresholds(profileId, target.Thresholds);
        foreach (var strategy in target.Strategies)
        {
            ValidateStrategy(profileId, strategy);
        }
        ValidateControlTypes(profileId, target.ExpectedControlTypes);
    }

    private static void ValidateThresholds(string profileId, ResolutionThresholds? thresholds)
    {
        if (thresholds is null || thresholds.MinimumConfidence is < 0 or > 1 ||
            thresholds.MinimumMargin is < 0 or > 1)
        {
            throw new ProfileValidationException($"Application profile '{profileId}' has invalid resolution thresholds.");
        }
    }

    private static void ValidateStrategy(string profileId, SelectorStrategy? strategy)
    {
        if (strategy is null || strategy.IsEmpty || !string.IsNullOrWhiteSpace(strategy.SemanticKey) ||
            strategy.Weight is < 0 or > 1 || strategy.Index is < 0)
        {
            throw new ProfileValidationException($"Application profile '{profileId}' has an invalid selector strategy.");
        }
        ValidateSelectorControlType(profileId, strategy.ControlType);
    }

    private static void ValidateControlTypes(string profileId, IReadOnlyList<string>? types)
    {
        if (types is null || types.Any(type => !KnownControlTypes.Contains(type)))
        {
            throw new ProfileValidationException($"Application profile '{profileId}' has invalid control types.");
        }
    }

    private static readonly HashSet<string> KnownControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown", "Button", "Calendar", "CheckBox", "ComboBox", "Edit", "Hyperlink", "Image",
        "ListItem", "List", "Menu", "MenuBar", "MenuItem", "ProgressBar", "RadioButton",
        "ScrollBar", "Slider", "Spinner", "StatusBar", "Tab", "TabItem", "Text", "ToolBar",
        "ToolTip", "Tree", "TreeItem", "Custom", "Group", "Thumb", "DataGrid", "DataItem",
        "Document", "SplitButton", "Window", "Pane", "Header", "HeaderItem", "Table",
        "TitleBar", "Separator", "SemanticZoom", "AppBar"
    };
}
