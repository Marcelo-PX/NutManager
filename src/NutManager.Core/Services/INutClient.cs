using NutManager.Core.Models;

namespace NutManager.Core.Services;

public interface INutClient
{
    Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(
        NutEndpoint endpoint,
        CancellationToken cancellationToken);

    Task<UpsSnapshot> GetSnapshotAsync(
        NutEndpoint endpoint,
        string upsName,
        CancellationToken cancellationToken);
}
