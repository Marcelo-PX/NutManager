using NutManager.Core.Administration;
using NutManager.Core.Models;

namespace NutManager.Core.Services;

public interface ILocalNutWindowsAdministration
{
    Task<NutWindowsAdministrationSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken);

    Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken);
}
