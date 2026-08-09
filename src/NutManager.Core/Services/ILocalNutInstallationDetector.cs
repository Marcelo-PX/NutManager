using NutManager.Core.Models;

namespace NutManager.Core.Services;

public interface ILocalNutInstallationDetector
{
    Task<NutInstallationInfo> DetectAsync(CancellationToken cancellationToken);

    Task<NutInstallationInfo> InspectDirectoryAsync(string installationOrConfigurationDirectory, CancellationToken cancellationToken);
}
