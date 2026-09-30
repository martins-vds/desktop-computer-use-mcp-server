using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Applications;

public sealed class ApplicationProfileStore : IApplicationProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IReadOnlyDictionary<string, ApplicationProfile> _profiles;

    public ApplicationProfileStore(IEnumerable<ApplicationProfile> profiles)
    {
        var dictionary = new Dictionary<string, ApplicationProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            ApplicationProfileValidator.Validate(profile);
            if (!dictionary.TryAdd(profile.Id, profile))
            {
                throw new ProfileValidationException(
                    $"Duplicate application profile ID '{profile.Id}'.");
            }
        }

        _profiles = dictionary;
    }

    public IReadOnlyList<ApplicationProfileSummary> List()
        => _profiles.Values
            .OrderBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new ApplicationProfileSummary(
                profile.Id,
                profile.DisplayName,
                profile.ExecutablePath,
                profile.Backend,
                profile.EnableScreenshots))
            .ToArray();

    public bool TryGet(string profileId, out ApplicationProfile profile)
        => _profiles.TryGetValue(profileId, out profile!);

    public static ApplicationProfileStore LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
        {
            return new ApplicationProfileStore([]);
        }

        var profiles = Directory
            .EnumerateFiles(fullDirectory, "*.application.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(LoadFile)
            .ToArray();

        return new ApplicationProfileStore(profiles);
    }

    public static ApplicationProfile LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        var profile = JsonSerializer.Deserialize<ApplicationProfile>(stream, SerializerOptions)
            ?? throw new ProfileValidationException($"Profile file '{path}' is empty.");

        var profileDirectory = Path.GetDirectoryName(path)
            ?? throw new ProfileValidationException($"Profile file '{path}' has no parent directory.");

        var executablePath = ResolvePath(profile.ExecutablePath, profileDirectory);
        var workingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory)
            ? Path.GetDirectoryName(executablePath)
            : ResolvePath(profile.WorkingDirectory, profileDirectory);

        var normalized = profile with
        {
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
