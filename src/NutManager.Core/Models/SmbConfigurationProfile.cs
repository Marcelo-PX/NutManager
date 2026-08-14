namespace NutManager.Core.Models;

/// <summary>
/// Selects the remote transport used only to access NUT configuration files.
/// Monitoring remains independent and uses the NUT TCP endpoint.
/// </summary>
public enum RemoteConfigurationTransportKind
{
    SshSftp,
    Smb
}

public enum SmbAuthenticationMode
{
    CurrentWindowsIdentity,
    ExplicitCredentials
}

/// <summary>
/// Non-secret SMB configuration metadata persisted with a managed remote profile.
/// The password is deliberately session-only and is never part of this model.
/// </summary>
public sealed record SmbConfigurationProfile
{
    public SmbConfigurationProfile(
        string sharePath,
        string? configurationDirectory = null,
        SmbAuthenticationMode authenticationMode = SmbAuthenticationMode.CurrentWindowsIdentity,
        string? username = null)
    {
        if (!Enum.IsDefined(authenticationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(authenticationMode), "The SMB authentication mode is invalid.");
        }

        SharePath = SmbUncPath.NormalizeShareRoot(sharePath);
        ConfigurationDirectory = SmbUncPath.NormalizeConfigurationDirectory(SharePath, configurationDirectory);
        AuthenticationMode = authenticationMode;

        // Optional in both modes. For explicit credentials the account is chosen in the Windows
        // credential dialog and recorded here afterwards, so a profile that has been set to use
        // another account but not yet signed in has no username. Requiring one would make that
        // ordinary intermediate state unrepresentable.
        Username = NutMonitoringProfile.NormalizeOptionalText(username, nameof(username), 255);
    }

    public string SharePath { get; }

    public string? ConfigurationDirectory { get; }

    public SmbAuthenticationMode AuthenticationMode { get; }

    public string? Username { get; }
}

/// <summary>
/// Pure UNC text validation. It intentionally does not use host filesystem APIs so
/// profile validation remains deterministic on every supported test runner.
/// </summary>
public static class SmbUncPath
{
    public static string NormalizeShareRoot(string value)
    {
        var parts = SplitUnc(value, nameof(value));
        if (parts.Length != 2)
        {
            throw new ArgumentException("The SMB share path must be a UNC share root.", nameof(value));
        }

        return $"\\\\{parts[0]}\\{parts[1]}";
    }

    public static string NormalizeUncPath(string value)
    {
        var parts = SplitUnc(value, nameof(value));
        return $"\\\\{string.Join('\\', parts)}";
    }

    public static string? NormalizeConfigurationDirectory(string shareRoot, string? configurationDirectory)
    {
        var normalizedShare = NormalizeShareRoot(shareRoot);
        if (string.IsNullOrWhiteSpace(configurationDirectory))
        {
            return null;
        }

        var parts = SplitUnc(configurationDirectory, nameof(configurationDirectory));
        if (parts.Length < 2 || !string.Equals(parts[0], GetServer(normalizedShare), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[1], GetShare(normalizedShare), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The SMB configuration directory must remain inside the configured share.", nameof(configurationDirectory));
        }

        return $"\\\\{string.Join('\\', parts)}";
    }

    public static bool IsWithinShare(string shareRoot, string path)
    {
        try
        {
            var normalizedShare = NormalizeShareRoot(shareRoot);
            var normalizedPath = NormalizeConfigurationDirectory(normalizedShare, path);
            return normalizedPath is not null;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string CombineDirectChild(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['\\', '/']) >= 0 || fileName.Contains("..", StringComparison.Ordinal) || fileName.Any(char.IsControl))
        {
            throw new ArgumentException("The SMB child name is invalid.", nameof(fileName));
        }

        return $"{NormalizeConfigurationDirectory(NormalizeShareRoot(GetShareRoot(directory)), directory)}\\{fileName}";
    }

    public static string? GetParentWithinShare(string shareRoot, string directory)
    {
        var normalizedShare = NormalizeShareRoot(shareRoot);
        var normalizedDirectory = NormalizeConfigurationDirectory(normalizedShare, directory)
            ?? throw new ArgumentException("The SMB directory is invalid.", nameof(directory));
        if (string.Equals(normalizedShare, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var separator = normalizedDirectory.LastIndexOf('\\');
        var parent = normalizedDirectory[..separator];
        return IsWithinShare(normalizedShare, parent) ? parent : null;
    }

    private static string GetShareRoot(string path)
    {
        var parts = SplitUnc(path, nameof(path));
        if (parts.Length < 2)
        {
            throw new ArgumentException("The SMB path is invalid.", nameof(path));
        }

        return $"\\\\{parts[0]}\\{parts[1]}";
    }

    private static string GetServer(string shareRoot) => SplitUnc(shareRoot, nameof(shareRoot))[0];

    private static string GetShare(string shareRoot) => SplitUnc(shareRoot, nameof(shareRoot))[1];

    private static string[] SplitUnc(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Any(char.IsControl) || !trimmed.StartsWith("\\\\", StringComparison.Ordinal) || trimmed.StartsWith("\\\\\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("The SMB UNC path is invalid.", parameterName);
        }

        var parts = trimmed[2..].Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Any(part => part is "." or ".." || part.Any(char.IsControl)) || parts[0] == ".")
        {
            throw new ArgumentException("The SMB UNC path is invalid.", parameterName);
        }

        return parts;
    }
}
