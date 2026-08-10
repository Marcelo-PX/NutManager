using NutManager.Core.Models;

namespace NutManager.Infrastructure.Remote.Ssh;

/// <summary>
/// Handles remote path text without applying the local host filesystem semantics.
/// </summary>
public static class RemotePathMapper
{
    public static string Combine(string directory, string childName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(childName);
        if (childName.IndexOfAny(['/', '\\']) >= 0 || childName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("A remote child name must not contain path segments.", nameof(childName));
        }

        return directory.TrimStart().StartsWith("\\\\", StringComparison.Ordinal)
            ? SmbUncPath.CombineDirectChild(directory, childName)
            : $"{directory.TrimEnd('/', '\\')}/{childName}";
    }

    public static string ToSftpPath(string remotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        var trimmed = remotePath.Trim();
        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("The remote path is invalid.", nameof(remotePath));
        }

        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return SmbUncPath.NormalizeUncPath(trimmed);
        }

        if (IsWindowsDrivePath(trimmed))
        {
            return NormalizeWindowsPath(trimmed);
        }

        return NormalizeNativePath(trimmed);
    }

    public static bool IsWindowsDrivePath(string remotePath) =>
        remotePath.Length >= 3 &&
        char.IsAsciiLetter(remotePath[0]) &&
        remotePath[1] == ':' &&
        remotePath[2] is '/' or '\\';

    private static string NormalizeWindowsPath(string path)
    {
        var parts = path[3..].Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        ValidateSegments(parts, nameof(path));
        return string.Concat(char.ToUpperInvariant(path[0]), ":/", string.Join('/', parts));
    }

    private static string NormalizeNativePath(string path)
    {
        var isAbsolute = path.StartsWith("/", StringComparison.Ordinal);
        if (!isAbsolute)
        {
            throw new ArgumentException("The remote path must be absolute.", nameof(path));
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        ValidateSegments(parts, nameof(path));
        return isAbsolute ? "/" + string.Join('/', parts) : string.Join('/', parts);
    }

    private static void ValidateSegments(IEnumerable<string> segments, string parameterName)
    {
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Remote path traversal is not allowed.", parameterName);
        }
    }
}
