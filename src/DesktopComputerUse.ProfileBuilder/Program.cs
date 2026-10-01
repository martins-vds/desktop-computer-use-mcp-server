using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopComputerUse.Automation;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Profiles;
using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Automation.Threading;
using DesktopComputerUse.Automation.Windows;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;
using Microsoft.Extensions.Logging;

if (OperatingSystem.IsWindows())
{
    Win32DesktopApi.InitializePerMonitorV2();
}

return await ProfileBuilderProgram.RunAsync(args);

internal static class ProfileBuilderProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await ExecuteCommandAsync(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> ExecuteCommandAsync(string[] args)
    {
        if (IsHelpRequest(args))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        return await ExecuteNamedCommandAsync(args);
    }

    private static bool IsHelpRequest(IReadOnlyList<string> args)
        => args.Count == 0 ||
            new[] { "-h", "--help", "help" }.Contains(
                args[0],
                StringComparer.Ordinal);

    private static async Task<int> ExecuteNamedCommandAsync(string[] args)
    {
        var handlers = new Dictionary<string, Func<Task<int>>>(StringComparer.Ordinal)
        {
            ["validate"] = () => Task.FromResult(Validate(args)),
            ["snapshot"] = () => SnapshotAsync(args),
            ["search"] = () => Task.FromResult(Search(args)),
            ["add-target"] = () => Task.FromResult(AddTarget(args)),
            ["diff"] = () => Task.FromResult(Diff(args)),
            ["apply"] = () => Task.FromResult(Apply(args))
        };
        return handlers.TryGetValue(args[0], out var handler)
            ? await handler()
            : Fail($"Unknown command '{args[0]}'.");
    }

    private static int Validate(string[] args)
    {
        RequireArguments(args, 2, "validate <profile>");
        var profile = ApplicationProfileStore.LoadFile(args[1]);
        Console.WriteLine(
            $"Valid profile '{profile.Id}', schema v{profile.SchemaVersion}, {profile.EffectiveSemanticTargets.Count} semantic targets.");
        return 0;
    }

    private static async Task<int> SnapshotAsync(string[] args)
    {
        RequireArguments(
            args,
            3,
            "snapshot <profile> <output.json> [--attach <pid>]");
        var profile = ApplicationProfileStore.LoadFile(args[1]);
        var processId = AttachedProcessId(args);

        await using var controller = CreateController(profile);
        var connection = processId is int pid
            ? await controller.AttachAsync(profile.Id, pid, CancellationToken.None)
            : await controller.LaunchAsync(profile.Id, CancellationToken.None);
        EnsureSucceeded(connection);

        try
        {
            var snapshot = await controller.SnapshotApplicationAsync(
                profile.MaxTreeDepth,
                profile.MaxResults,
                CancellationToken.None);
            EnsureSucceeded(snapshot);
            await WriteJsonAsync(args[2], snapshot.Value!);
            Console.WriteLine(
                $"Wrote {snapshot.Value!.Window.Controls.Count} controls for view {snapshot.Value.Window.View.Key}.");
        }

        finally
        {
            await controller.DetachAsync(
                terminateOwnedProcess: processId is null,
                CancellationToken.None);
        }

        return 0;
    }

    private static int? AttachedProcessId(string[] args)
    {
        var attachIndex = Array.IndexOf(args, "--attach");
        return attachIndex >= 0
            ? int.Parse(args.ElementAtOrDefault(attachIndex + 1)
                ?? throw new ArgumentException("--attach requires a process ID."))
            : null;
    }

    private static int Search(string[] args)
    {
        RequireArguments(
            args,
            4,
            "search <profile> <snapshot.json> <semantic-key>");
        var profile = ApplicationProfileStore.LoadFile(args[1]);
        var snapshot = ReadJson<ApplicationSnapshot>(args[2]);
        if (!profile.EffectiveSemanticTargets.TryGetValue(args[3], out var target))
        {
            return Fail($"Semantic target '{args[3]}' is not defined.");
        }

        var result = new FuzzyControlResolver().Resolve(
            snapshot,
            args[3],
            target,
            20);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return result.Status == ResolutionStatus.NotFound ? 2 : 0;
    }

    private static int AddTarget(string[] args)
    {
        RequireArguments(
            args,
            7,
            "add-target <profile> <snapshot.json> <semantic-key> <intent> <candidate-id> <output-profile> [--pattern <pattern>]");
        var profile = ApplicationProfileStore.LoadFile(args[1]);
        var snapshot = ReadJson<ApplicationSnapshot>(args[2]);
        var patternIndex = Array.IndexOf(args, "--pattern");
        var requiredPatterns = patternIndex >= 0
            ? (args.ElementAtOrDefault(patternIndex + 1)
                ?? throw new ArgumentException("--pattern requires a value."))
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        var updated = new ProfileDraftBuilder().AddTarget(
            profile,
            snapshot,
            args[3],
            args[4],
            args[5],
            requiredPatterns);
        ApplicationProfileStore.SaveFile(args[6], updated);
        Console.WriteLine(
            $"Added semantic target '{args[3]}' for {args[5]} to '{Path.GetFullPath(args[6])}'.");
        return 0;
    }

    private static int Diff(string[] args)
    {
        RequireArguments(args, 3, "diff <original-profile> <draft-profile>");
        var original = ApplicationProfileStore.LoadFile(args[1]);
        var draft = ApplicationProfileStore.LoadFile(args[2]);
        foreach (var difference in ProfileDiff.Compare(original, draft, JsonOptions))
        {
            Console.WriteLine(difference);
        }

        return 0;
    }

    private static int Apply(string[] args)
    {
        RequireArguments(args, 3, "apply <draft-profile> <target-profile>");
        var draft = ApplicationProfileStore.LoadFile(args[1]);
        ApplicationProfileStore.SaveFile(args[2], draft);
        Console.WriteLine($"Applied validated profile to '{Path.GetFullPath(args[2])}'.");
        return 0;
    }

    private static DesktopAutomationController CreateController(
        ApplicationProfile profile)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.UseUtcTimestamp = true;
            }));
        var observer = new ControlObserver();
        return new DesktopAutomationController(
            new ApplicationProfileStore([profile]),
            new MtaAutomationWorker(),
            new FlaUiAutomationFactory(),
            new ControlSelectorResolver(),
            observer,
            new ApplicationSnapshotBuilder(observer),
            new FuzzyControlResolver(),
            loggerFactory.CreateLogger<DesktopAutomationController>());
    }

    private static T ReadJson<T>(string path)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"File '{path}' did not contain valid JSON.");

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Output '{path}' has no parent directory."));
        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void EnsureSucceeded<T>(AutomationResult<T> result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"{result.Error?.Code}: {result.Error?.Message}");
        }
    }

    private static void RequireArguments(
        IReadOnlyCollection<string> args,
        int count,
        string usage)
    {
        if (args.Count < count)
        {
            throw new ArgumentException($"Usage: profile-builder {usage}");
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Desktop Computer Use profile builder

              validate <profile>
              snapshot <profile> <output.json> [--attach <pid>]
              search <profile> <snapshot.json> <semantic-key>
              add-target <profile> <snapshot.json> <semantic-key> <intent> <candidate-id> <output-profile> [--pattern <pattern>]
              diff <original-profile> <draft-profile>
              apply <draft-profile> <target-profile>
            """);
    }
}
