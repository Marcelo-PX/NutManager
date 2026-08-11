using NutManager.Core.Models;

namespace NutManager.Core.Services;

public interface IManagedNutConnectionTester
{
    Task<ManagedNutConnectionTestResult> TestAsync(
        NutEndpoint endpoint,
        string? preferredUpsName,
        CancellationToken cancellationToken);
}
