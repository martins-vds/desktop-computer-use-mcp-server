using DesktopComputerUse.Automation;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Automation.Threading;
using DesktopComputerUse.Server.Tools;
using DesktopComputerUse.Server.Prompts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<IApplicationProfileStore>(_ =>
{
    var configuredDirectory =
        builder.Configuration["DesktopComputerUse:ProfilesDirectory"] ??
        Environment.GetEnvironmentVariable("DESKTOP_COMPUTER_USE_PROFILES") ??
        "profiles";
    return ApplicationProfileStore.LoadFromDirectory(configuredDirectory);
});
builder.Services.AddSingleton<IAutomationWorker, MtaAutomationWorker>();
builder.Services.AddSingleton<FlaUiAutomationFactory>();
builder.Services.AddSingleton<ControlSelectorResolver>();
builder.Services.AddSingleton<ControlObserver>();
builder.Services.AddSingleton<ApplicationSnapshotBuilder>();
builder.Services.AddSingleton<FuzzyControlResolver>();
builder.Services.AddSingleton<DesktopAutomationController>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ApplicationTools>()
    .WithTools<ControlTools>()
    .WithPrompts<ProfilePrompts>();

await builder.Build().RunAsync();
