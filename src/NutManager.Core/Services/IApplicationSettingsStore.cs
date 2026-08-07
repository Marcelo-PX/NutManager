using NutManager.Core.Models;

namespace NutManager.Core.Services;

public interface IApplicationSettingsStore
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken);
}
