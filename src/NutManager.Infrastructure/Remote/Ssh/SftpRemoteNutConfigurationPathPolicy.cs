using NutManager.Core.Services;

namespace NutManager.Infrastructure.Remote.Ssh;

/// <summary>
/// SSH/SFTP path policy. SFTP path comparison remains ordinal, matching the
/// semantics used by the existing remote session.
/// </summary>
public sealed class SftpRemoteNutConfigurationPathPolicy : IRemoteNutConfigurationPathPolicy
{
    public static SftpRemoteNutConfigurationPathPolicy Instance { get; } = new();

    private SftpRemoteNutConfigurationPathPolicy()
    {
    }

    public string NormalizeDirectory(string directory) => RemotePathMapper.ToSftpPath(directory);

    public string NormalizePath(string path) => RemotePathMapper.ToSftpPath(path);

    public string CombineDirectChild(string directory, string childName) =>
        RemotePathMapper.Combine(NormalizeDirectory(directory), childName);

    public bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.Ordinal);

    public string? GetParentDirectory(string directory)
    {
        var normalized = NormalizeDirectory(directory);
        var slash = normalized.TrimEnd('/').LastIndexOf('/');
        return slash < 0 ? null : slash == 0 ? "/" : normalized[..slash];
    }
}
