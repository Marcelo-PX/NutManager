using NutManager.Core.Models;

namespace NutManager.Core.Services;

/// <summary>Passively reports known driver executables found inside a detected local NUT installation.</summary>
public interface ILocalNutDriverCatalogSource
{
    Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(NutInstallationInfo installation, CancellationToken cancellationToken);
}
