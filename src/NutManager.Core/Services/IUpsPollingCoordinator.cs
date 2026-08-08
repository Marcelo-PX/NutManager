using NutManager.Core.Models;

namespace NutManager.Core.Services;

public interface IUpsPollingCoordinator : IDisposable
{
    PollingState State { get; }
    event Action<PollingState>? StateChanged;
    Task MonitorAsync(string? upsName, CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
