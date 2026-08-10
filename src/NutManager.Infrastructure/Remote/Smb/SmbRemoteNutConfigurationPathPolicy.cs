using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Remote.Smb;

/// <summary>
/// UNC policy scoped to the explicitly configured SMB share. UNC comparisons are
/// case-insensitive and never inherit the current host filesystem semantics.
/// </summary>
public sealed class SmbRemoteNutConfigurationPathPolicy : IRemoteNutConfigurationPathPolicy
{
    private readonly string _shareRoot;

    public SmbRemoteNutConfigurationPathPolicy(string shareRoot)
    {
        _shareRoot = SmbUncPath.NormalizeShareRoot(shareRoot);
    }

    public string NormalizeDirectory(string directory) =>
        SmbUncPath.NormalizeConfigurationDirectory(_shareRoot, directory)
        ?? throw new ArgumentException("An SMB configuration directory is required.", nameof(directory));

    public string NormalizePath(string path)
    {
        var normalized = SmbUncPath.NormalizeUncPath(path);
        if (!SmbUncPath.IsWithinShare(_shareRoot, normalized))
        {
            throw new ArgumentException("The SMB path is outside the configured share.", nameof(path));
        }

        return normalized;
    }

    public string CombineDirectChild(string directory, string childName) =>
        SmbUncPath.CombineDirectChild(NormalizeDirectory(directory), childName);

    public bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    public string? GetParentDirectory(string directory) =>
        SmbUncPath.GetParentWithinShare(_shareRoot, NormalizeDirectory(directory));
}
