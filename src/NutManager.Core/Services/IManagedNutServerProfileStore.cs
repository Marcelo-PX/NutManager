using NutManager.Core.Models;

namespace NutManager.Core.Services;

public interface IManagedNutServerProfileStore
{
    Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken);
}
