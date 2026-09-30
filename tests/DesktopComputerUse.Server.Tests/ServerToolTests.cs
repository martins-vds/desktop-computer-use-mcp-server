using System.Reflection;
using DesktopComputerUse.Automation;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Automation.Threading;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Server.Prompts;
using DesktopComputerUse.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace DesktopComputerUse.Server.Tests;

public sealed class ServerToolTests
{
    [Fact]
    public async Task Application_tools_delegate_profile_listing_and_unknown_launch()
    {
        var profile = CreateProfile("calculator");
        var worker = new RecordingWorker();
        await using var controller = CreateController(
            new ApplicationProfileStore([profile]),
            worker);

        var listed = ApplicationTools.ListApplicationProfiles(controller);
        var launch = await ApplicationTools.LaunchApplication(
            controller,
            "missing",
            CancellationToken.None);

        var summary = Assert.Single(listed);
        Assert.Equal(profile.Id, summary.Id);
        Assert.False(launch.Succeeded);
        Assert.Equal(AutomationErrorCode.ProfileNotFound, launch.Error?.Code);
        Assert.Equal(0, worker.RunCount);
    }

    [Fact]
    public async Task Application_and_control_tools_preserve_controller_failures()
    {
        var worker = new RecordingWorker();
        await using var controller = CreateController(
            new ApplicationProfileStore([]),
            worker);

        var state = await ApplicationTools.GetApplicationState(
            controller,
            CancellationToken.None);
        var control = await ControlTools.FindControl(
            controller,
            new ControlSelector { AutomationId = "SaveButton" },
            CancellationToken.None);
        var snapshot = await ControlTools.SnapshotApplicationSchema(
            controller,
            maxDepth: 3,
            maxResults: 25,
            CancellationToken.None);
        var resolution = await ControlTools.ResolveControlIntent(
            controller,
            "save",
            maximumCandidates: 5,
            CancellationToken.None);

        Assert.False(state.Succeeded);
        Assert.Equal(AutomationErrorCode.ApplicationNotAttached, state.Error?.Code);
        Assert.False(control.Succeeded);
        Assert.Equal(AutomationErrorCode.ApplicationNotAttached, control.Error?.Code);
        Assert.False(snapshot.Succeeded);
        Assert.Equal(AutomationErrorCode.ApplicationNotAttached, snapshot.Error?.Code);
        Assert.False(resolution.Succeeded);
        Assert.Equal(AutomationErrorCode.ApplicationNotAttached, resolution.Error?.Code);
        Assert.Equal(4, worker.RunCount);
    }

    [Fact]
    public void Tool_types_expose_expected_mcp_names_and_structured_content()
    {
        var discovered = new[] { typeof(ApplicationTools), typeof(ControlTools) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => new
            {
                Method = method,
                Attribute = method.GetCustomAttribute<McpServerToolAttribute>()
            })
            .Where(item => item.Attribute is not null)
            .ToDictionary(
                item => item.Attribute!.Name!,
                item => item,
                StringComparer.Ordinal);

        var expectedNames = new[]
        {
            "list_application_profiles",
            "launch_application",
            "attach_application",
            "detach_application",
            "get_application_state",
            "capture_application_window",
            "snapshot_application_schema",
            "resolve_control_intent",
            "get_profile_update_proposals",
            "export_profile_update_patch",
            "capture_control_image",
            "inspect_controls",
            "find_control",
            "get_control_properties",
            "invoke_control",
            "set_control_value",
            "select_control_item",
            "set_expanded_state",
            "scroll_control",
            "wait_for_state"
        };

        Assert.Equal(expectedNames.Order(), discovered.Keys.Order());
        Assert.All(
            discovered.Values.Where(item => item.Attribute!.Name != "capture_control_image"),
            item => Assert.True(item.Attribute!.UseStructuredContent));
        Assert.False(discovered["capture_control_image"].Attribute!.UseStructuredContent);
        Assert.All(
            new[] { typeof(ApplicationTools), typeof(ControlTools) },
            type => Assert.NotNull(type.GetCustomAttribute<McpServerToolTypeAttribute>()));
    }

    [Fact]
    public void Mcp_registration_generates_client_schemas_without_injected_parameters()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateController(
            new ApplicationProfileStore([CreateProfile("app")]),
            new RecordingWorker()));
        services
            .AddMcpServer()
            .WithTools<ApplicationTools>()
            .WithTools<ControlTools>();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>()
            .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);

        Assert.Equal(20, tools.Count);
        AssertSchemaProperties(
            tools["launch_application"],
            required: ["profileId"],
            properties: ["profileId"]);
        AssertSchemaProperties(
            tools["set_control_value"],
            required: ["selector", "value"],
            properties: ["selector", "value"]);
        AssertSchemaProperties(
            tools["detach_application"],
            required: [],
            properties: ["terminateOwnedProcess"]);
        AssertSchemaProperties(
            tools["snapshot_application_schema"],
            required: [],
            properties: ["maxDepth", "maxResults"]);
        AssertSchemaProperties(
            tools["resolve_control_intent"],
            required: ["semanticKey"],
            properties: ["maximumCandidates", "semanticKey"]);
        AssertSchemaProperties(
            tools["capture_control_image"],
            required: ["selector"],
            properties: ["selector"]);
    }

    [Fact]
    public void Profile_prompts_expose_read_only_authoring_workflows()
    {
        var promptNames = typeof(ProfilePrompts)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<McpServerPromptAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name)
            .Order()
            .ToArray();

        Assert.Equal(
            new[]
            {
                "add_semantic_target",
                "profile_application",
                "review_profile_healing"
            },
            promptNames);
        Assert.NotNull(typeof(ProfilePrompts).GetCustomAttribute<McpServerPromptTypeAttribute>());
    }

    private static void AssertSchemaProperties(
        McpServerTool tool,
        IReadOnlyCollection<string> required,
        IReadOnlyCollection<string> properties)
    {
        var schema = tool.ProtocolTool.InputSchema;
        var schemaProperties = schema.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ToArray();
        var schemaRequired = schema.TryGetProperty("required", out var requiredElement)
            ? requiredElement.EnumerateArray()
                .Select(item => item.GetString()!)
                .Order()
                .ToArray()
            : [];

        Assert.Equal(properties.Order(), schemaProperties);
        Assert.Equal(required.Order(), schemaRequired);
        Assert.DoesNotContain("controller", schemaProperties);
        Assert.DoesNotContain("cancellationToken", schemaProperties);
    }

    private static ApplicationProfile CreateProfile(string id)
        => new()
        {
            Id = id,
            DisplayName = id,
            ExecutablePath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, $"{id}.exe"))
        };

    private static DesktopAutomationController CreateController(
        IApplicationProfileStore profiles,
        IAutomationWorker worker)
    {
        var observer = new ControlObserver();
        return new DesktopAutomationController(
            profiles,
            worker,
            new FlaUiAutomationFactory(),
            new ControlSelectorResolver(),
            observer,
            new ApplicationSnapshotBuilder(observer),
            new FuzzyControlResolver(),
            NullLogger<DesktopAutomationController>.Instance);
    }

    private sealed class RecordingWorker : IAutomationWorker
    {
        public int RunCount { get; private set; }

        public Task<T> RunAsync<T>(
            Func<CancellationToken, T> action,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            RunCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(action(cancellationToken));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
