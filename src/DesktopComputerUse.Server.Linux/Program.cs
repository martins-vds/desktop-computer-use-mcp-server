using DesktopComputerUse.Server.Linux;
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

builder.Services.AddSingleton(_ =>
{
    var configuredDirectory =
        builder.Configuration["DesktopComputerUse:ProfilesDirectory"] ??
        Environment.GetEnvironmentVariable("DESKTOP_COMPUTER_USE_PROFILES") ??
        "profiles";
    return PortableProfileStore.LoadFromDirectory(configuredDirectory);
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<LinuxApplicationTools>()
    .WithTools<LinuxControlTools>();

await builder.Build().RunAsync();
