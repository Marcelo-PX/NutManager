using System.Reflection;
using System.Runtime.InteropServices;

namespace NutManager.App.Services;

public sealed record ApplicationRuntimeInfo(
    string Version,
    string Runtime,
    string OperatingSystem,
    string Architecture)
{
    private const string UnavailableText = "Indisponível";

    public static ApplicationRuntimeInfo CreateCurrent()
    {
        var assembly = typeof(ApplicationRuntimeInfo).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? UnavailableText
            : informationalVersion;

        return new ApplicationRuntimeInfo(
            version,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
    }
}
