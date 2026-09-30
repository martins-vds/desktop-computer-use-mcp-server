using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopComputerUse.Contracts.Configuration;

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

    private readonly IReadOnlyList<ApplicationProfileSummary> _profiles;

    private PortableProfileStore(IEnumerable<ApplicationProfileSummary> profiles)
    {
        _profiles = profiles
            .OrderBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ApplicationProfileSummary> List() => _profiles;

    public static PortableProfileStore LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
        {
            return new PortableProfileStore([]);
        }

        var profiles = Directory
            .EnumerateFiles(fullDirectory, "*.application.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(LoadSummary)
            .ToArray();

        return new PortableProfileStore(profiles);
    }

    private static ApplicationProfileSummary LoadSummary(string path)
    {
        using var stream = File.OpenRead(path);
        var profile = JsonSerializer.Deserialize<ApplicationProfile>(stream, SerializerOptions)
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

        return new ApplicationProfileSummary(
            profile.Id,
            profile.DisplayName,
            profile.ExecutablePath,
            profile.Backend,
            profile.EnableScreenshots);
    }
}
