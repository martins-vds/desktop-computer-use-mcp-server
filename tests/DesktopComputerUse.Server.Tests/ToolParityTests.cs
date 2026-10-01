using System.Reflection;
using System.Runtime.Loader;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Server.Tools;
using ModelContextProtocol.Server;

namespace DesktopComputerUse.Server.Tests;

public sealed class ToolParityTests
{
    [Fact]
    public void Linux_and_windows_expose_identical_tool_names_and_client_parameters()
    {
        var context = new AssemblyLoadContext("LinuxToolParity", isCollectible: true);
        try
        {
            var linux = context.LoadFromAssemblyPath(
                Path.Combine(AppContext.BaseDirectory, "Linux", "desktop-computer-use.dll"));
            var windows = typeof(ApplicationTools).Assembly;
            var windowsTools = Discover(windows);
            var linuxTools = Discover(linux);
            Assert.Equal(windowsTools.Keys.Order(), linuxTools.Keys.Order());
            foreach (var name in windowsTools.Keys)
            {
                Assert.Equal(ClientParameters(windowsTools[name]), ClientParameters(linuxTools[name]));
                Assert.Equal(
                    windowsTools[name].GetCustomAttribute<McpServerToolAttribute>()!.UseStructuredContent,
                    linuxTools[name].GetCustomAttribute<McpServerToolAttribute>()!.UseStructuredContent);
            }
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Linux_native_tools_return_platform_errors()
    {
        var context = new AssemblyLoadContext("LinuxNativeErrors", isCollectible: true);
        try
        {
            var linux = context.LoadFromAssemblyPath(
                Path.Combine(AppContext.BaseDirectory, "Linux", "desktop-computer-use.dll"));
            foreach (var method in Discover(linux).Values.Where(method =>
                         method.DeclaringType!.Name == "LinuxNativeTools" &&
                         method.Name != "ReloadApplicationProfiles" &&
                         method.GetCustomAttribute<McpServerToolAttribute>()!.UseStructuredContent))
            {
                var arguments = method.GetParameters().Select(parameter =>
                    parameter.HasDefaultValue ? parameter.DefaultValue :
                    parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) :
                    parameter.ParameterType == typeof(string) ? string.Empty : null).ToArray();
                var result = method.Invoke(null, arguments)!;
                Assert.False((bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!);
                var error = (AutomationError)result.GetType().GetProperty("Error")!.GetValue(result)!;
                Assert.Equal(AutomationErrorCode.PlatformNotSupported, error.Code);
            }
        }
        finally
        {
            context.Unload();
        }
    }

    private static Dictionary<string, MethodInfo> Discover(Assembly assembly)
        => assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToDictionary(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name!,
                StringComparer.Ordinal);

    private static string[] ClientParameters(MethodInfo method)
        => method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken) &&
                parameter.ParameterType.Name is not ("DesktopAutomationController" or "PortableProfileStore"))
            .Select(parameter => $"{parameter.Name}:{parameter.ParameterType.FullName}:{parameter.HasDefaultValue}")
            .ToArray();
}
