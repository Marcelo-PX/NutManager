namespace NutManager.Core.Models;

public sealed record NutInstallationInfo(
    bool IsDetected,
    string? InstallationDirectory,
    string? ConfigurationDirectory,
    string? Version,
    IReadOnlyDictionary<string, string> Executables,
    IReadOnlyList<NutConfigurationFileInfo> ConfigurationFiles,
    string? DetectionSource,
    string? ErrorMessage = null)
{
    public static NutInstallationInfo NotDetected(string? detectionSource = null, string? errorMessage = null) => new(
        false,
        null,
        null,
        null,
        new Dictionary<string, string>(),
        Array.Empty<NutConfigurationFileInfo>(),
        detectionSource,
        errorMessage);
}

public sealed record NutConfigurationFileInfo(
    string Name,
    string FullPath,
    bool Exists,
    bool IsReadable);
