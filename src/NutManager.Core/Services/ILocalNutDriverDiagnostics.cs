using NutManager.Core.Administration;
using NutManager.Core.Models;

namespace NutManager.Core.Services;

/// <summary>
/// Provides the explicitly allowlisted local NUT driver diagnostics.
/// </summary>
public interface ILocalNutDriverDiagnostics
{
    Task<NutDriverDiagnosticsSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken);

    Task<NutDriverDiagnosticResult> ExecuteAsync(NutDriverDiagnosticRequest request, CancellationToken cancellationToken);
}
