using NutManager.Core.Models;

namespace NutManager.Core.Services;

public enum NutVersionSource
{
    Unavailable,
    FileMetadata,
    ExecutableFallback
}

public sealed record NutVersionResolution(string? Version, NutVersionSource Source)
{
    public static NutVersionResolution Unavailable { get; } = new(null, NutVersionSource.Unavailable);
}

public interface ILocalNutVersionResolver
{
    Task<NutVersionResolution> ResolveAsync(NutInstallationInfo installation, CancellationToken cancellationToken);
}
