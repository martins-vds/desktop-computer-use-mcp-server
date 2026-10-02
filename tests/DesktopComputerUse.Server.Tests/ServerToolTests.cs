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
        var value = await ControlTools.GetControlValue(
            controller, new ControlSelector { AutomationId = "CustomerNameTextBox" },
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
        Assert.False(value.Succeeded);
        Assert.Equal(AutomationErrorCode.ApplicationNotAttached, value.Error?.Code);
        Assert.False(snapshot.Succeeded);
        Assert.Equal(AutomationErrorCode.ApplicationNotAttached, snapshot.Error?.Code);
        Assert.False(resolution.Succeeded);
        Assert.Equal(AutomationErrorCode.ApplicationNotAttached, resolution.Error?.Code);
        Assert.Equal(5, worker.RunCount);
    }

    [Fact]
    public async Task Native_tools_preserve_missing_session_errors_without_dispatch()
    {
        var worker = new RecordingWorker();
        await using var controller = CreateController(new ApplicationProfileStore([]), worker);
        var geometry = await NativeDesktopTools.GetWindowGeometry(controller, CancellationToken.None);
        var restore = await NativeDesktopTools.RestoreWindow(controller, CancellationToken.None);
        var activate = await NativeDesktopTools.ActivateWindow(controller);
        var click = await NativeDesktopTools.ClickAtPoint(controller, -100, 10);
        var captureClick = await NativeDesktopTools.ClickCapturePoint(controller, "missing", 1, 2);
        var type = await NativeDesktopTools.TypeText(controller, "secret", CancellationToken.None);
        var key = await NativeDesktopTools.KeyPress(controller, "ctrl+s", CancellationToken.None);
        Assert.All(new[] { geometry.Error, restore.Error, activate.Error, click.Error,
                captureClick.Error, type.Error, key.Error },
            error => Assert.Equal(AutomationErrorCode.ApplicationNotAttached, error!.Code));
        Assert.Equal(7, worker.RunCount);
        var exception = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() =>
            NativeDesktopTools.CaptureApplicationWindowImage(controller, CancellationToken.None));
        Assert.Contains("No application is currently attached", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
    }

    [Fact]
    public async Task Reload_tool_preserves_invalid_registry_and_returns_an_explicit_failure()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DesktopComputerUse-ServerReload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "app.application.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(CreateProfile("app")));
            var store = ApplicationProfileStore.LoadFromDirectory(directory);
            await using var controller = CreateController(store, new RecordingWorker());
            var valid = await NativeDesktopTools.ReloadApplicationProfiles(controller, CancellationToken.None);
            Assert.True(valid.Succeeded);
            Assert.True(valid.Value!.Succeeded);
            var revision = Assert.Single(controller.ListProfiles()).Revision;
            File.WriteAllText(path, "{invalid");
            var invalid = await NativeDesktopTools.ReloadApplicationProfiles(controller, CancellationToken.None);
            Assert.False(invalid.Succeeded);
            Assert.Equal(AutomationErrorCode.InvalidProfile, invalid.Error!.Code);
            Assert.False(invalid.Value!.Succeeded);
            Assert.Single(invalid.Value.Failures);
            Assert.Equal(revision, Assert.Single(controller.ListProfiles()).Revision);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Tool_types_expose_expected_mcp_names_and_structured_content()
    {
        var discovered = new[] { typeof(ApplicationTools), typeof(ControlTools), typeof(NativeDesktopTools) }
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
            "get_control_value",
            "invoke_control",
            "set_control_value",
            "select_control_item",
            "set_expanded_state",
            "scroll_control",
            "wait_for_state",
            "reload_application_profiles",
            "get_desktop_layout",
            "get_window_geometry",
            "restore_window",
            "activate_window",
            "click_at_point",
            "type_text",
            "key_press",
            "click_capture_point",
            "capture_application_window_image"
        };

        Assert.Equal(expectedNames.Order(), discovered.Keys.Order());
        Assert.All(
            discovered.Values.Where(item => item.Attribute!.Name is not ("capture_control_image" or "capture_application_window_image")),
            item => Assert.True(item.Attribute!.UseStructuredContent));
        Assert.False(discovered["capture_control_image"].Attribute!.UseStructuredContent);
        Assert.False(discovered["capture_application_window_image"].Attribute!.UseStructuredContent);
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
            .WithTools<ControlTools>()
            .WithTools<NativeDesktopTools>();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>()
            .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);

        Assert.Equal(31, tools.Count);
        AssertSchemaProperties(tools["get_control_value"],
            required: ["selector"], properties: ["selector"]);
        AssertSchemaProperties(
            tools["launch_application"],
            required: ["profileId"],
            properties: ["ifAlreadyRunning", "onLaunchFailure", "profileId"]);
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
        AssertSchemaProperties(tools["click_at_point"], required: ["x", "y"],
            properties: ["button", "coordinateSpace", "x", "y"]);
        AssertSchemaProperties(tools["click_capture_point"], required: ["captureId", "x", "y"],
            properties: ["button", "captureId", "x", "y"]);
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
