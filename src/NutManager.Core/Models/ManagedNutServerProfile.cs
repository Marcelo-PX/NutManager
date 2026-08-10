namespace NutManager.Core.Models;

public enum NutManagementMode
{
    Local,
    Remote
}

public enum ManagedNutServerAccessMode
{
    ReadOnly,
    Manage
}

public sealed record NutMonitoringProfile
{
    public NutMonitoringProfile(string host, int port = NutEndpoint.DefaultPort, string? preferredUpsName = null)
    {
        Host = ValidateRequiredText(host, nameof(host), 255);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The port must be between 1 and 65535.");
        }

        Port = port;
        PreferredUpsName = NormalizeOptionalText(preferredUpsName, nameof(preferredUpsName), 255);
    }

    public string Host { get; }

    public int Port { get; }

    public string? PreferredUpsName { get; }

    internal static string ValidateRequiredText(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The value is invalid.", parameterName);
        }

        return normalized;
    }

    internal static string? NormalizeOptionalText(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ValidateRequiredText(value, parameterName, maximumLength);
    }
}

public sealed record NutManagementProfile
{
    public const int DefaultSshPort = 22;

    public NutManagementProfile(
        NutManagementMode mode,
        string? managementHost = null,
        string? remoteConfigurationDirectory = null,
        int sshPort = DefaultSshPort,
        string? sshUsername = null,
        string? trustedHostKeyFingerprint = null,
        string? trustedHostKeyAlgorithm = null,
        RemoteConfigurationTransportKind configurationTransport = RemoteConfigurationTransportKind.SshSftp,
        string? smbSharePath = null,
        string? smbConfigurationDirectory = null,
        SmbAuthenticationMode smbAuthenticationMode = SmbAuthenticationMode.CurrentWindowsIdentity,
        string? smbUsername = null)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "The management mode is invalid.");
        }

        Mode = mode;
        if (mode == NutManagementMode.Remote && !Enum.IsDefined(configurationTransport))
        {
            throw new ArgumentOutOfRangeException(nameof(configurationTransport), "The remote configuration transport is invalid.");
        }

        Mode = mode;
        ConfigurationTransport = mode == NutManagementMode.Remote ? configurationTransport : RemoteConfigurationTransportKind.SshSftp;
        if (mode == NutManagementMode.Remote && configurationTransport == RemoteConfigurationTransportKind.SshSftp)
        {
            ManagementHost = NutMonitoringProfile.ValidateRequiredText(managementHost!, nameof(managementHost), 255);
            RemoteConfigurationDirectory = ValidateRemoteDirectory(remoteConfigurationDirectory);
            if (sshPort is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(sshPort), "The SSH port must be between 1 and 65535.");
            }

            SshPort = sshPort;
            SshUsername = NutMonitoringProfile.NormalizeOptionalText(sshUsername, nameof(sshUsername), 255);
            TrustedHostKeyFingerprint = ValidateHostKeyFingerprint(trustedHostKeyFingerprint);
            TrustedHostKeyAlgorithm = TrustedHostKeyFingerprint is null
                ? null
                : NutMonitoringProfile.NormalizeOptionalText(trustedHostKeyAlgorithm, nameof(trustedHostKeyAlgorithm), 128);
            Smb = null;
        }
        else if (mode == NutManagementMode.Remote)
        {
            ManagementHost = null;
            RemoteConfigurationDirectory = null;
            SshPort = DefaultSshPort;
            SshUsername = null;
            TrustedHostKeyFingerprint = null;
            TrustedHostKeyAlgorithm = null;
            Smb = new SmbConfigurationProfile(smbSharePath!, smbConfigurationDirectory, smbAuthenticationMode, smbUsername);
        }
        else
        {
            ManagementHost = null;
            RemoteConfigurationDirectory = null;
            SshPort = DefaultSshPort;
            SshUsername = null;
            TrustedHostKeyFingerprint = null;
            TrustedHostKeyAlgorithm = null;
            Smb = null;
        }
    }

    public NutManagementMode Mode { get; }

    public RemoteConfigurationTransportKind ConfigurationTransport { get; }

    public string? ManagementHost { get; }

    public string? RemoteConfigurationDirectory { get; }

    public int SshPort { get; }

    public string? SshUsername { get; }

    public string? TrustedHostKeyFingerprint { get; }

    public string? TrustedHostKeyAlgorithm { get; }

    public SmbConfigurationProfile? Smb { get; }

    public string? SmbSharePath => Smb?.SharePath;

    public string? SmbConfigurationDirectory => Smb?.ConfigurationDirectory;

    public SmbAuthenticationMode SmbAuthenticationMode => Smb?.AuthenticationMode ?? global::NutManager.Core.Models.SmbAuthenticationMode.CurrentWindowsIdentity;

    public string? SmbUsername => Smb?.Username;

    private static string? ValidateRemoteDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 1024 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The remote configuration directory is invalid.", nameof(value));
        }

        return normalized;
    }

    private static string? ValidateHostKeyFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (!IsCanonicalSha256Fingerprint(normalized))
        {
            throw new ArgumentException("The trusted host key fingerprint is invalid.", nameof(value));
        }

        return normalized;
    }

    private static bool IsCanonicalSha256Fingerprint(string value)
    {
        const string prefix = "SHA256:";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length > 512 || value.Any(char.IsControl))
        {
            return false;
        }

        var encoded = value[prefix.Length..];
        if (encoded.Length != 43 || encoded.Contains('='))
        {
            return false;
        }

        try
        {
            var hash = Convert.FromBase64String(encoded + "=");
            return hash.Length == 32 && string.Equals(Convert.ToBase64String(hash).TrimEnd('='), encoded, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record ManagedNutServerProfile
{
    public ManagedNutServerProfile(
        Guid id,
        string name,
        NutMonitoringProfile monitoring,
        NutManagementProfile management,
        ManagedNutServerAccessMode accessMode)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The profile identifier is required.", nameof(id));
        }

        if (!Enum.IsDefined(accessMode))
        {
            throw new ArgumentOutOfRangeException(nameof(accessMode), "The access mode is invalid.");
        }

        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(management);

        Id = id;
        Name = NutMonitoringProfile.ValidateRequiredText(name, nameof(name), 80);
        Monitoring = monitoring;
        Management = management;
        AccessMode = accessMode;
    }

    public Guid Id { get; }

    public string Name { get; }

    public NutMonitoringProfile Monitoring { get; }

    public NutManagementProfile Management { get; }

    public ManagedNutServerAccessMode AccessMode { get; }
}

public sealed record ManagedNutServerProfiles
{
    public const int CurrentSchemaVersion = 3;

    public ManagedNutServerProfiles(int schemaVersion, Guid activeProfileId, IReadOnlyList<ManagedNutServerProfile> profiles)
    {
        if (schemaVersion is < 1 or > CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Unsupported managed server profile schema version.");
        }

        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0)
        {
            throw new ArgumentException("At least one profile is required.", nameof(profiles));
        }

        if (profiles.Any(profile => profile is null))
        {
            throw new ArgumentException("Profiles cannot contain null values.", nameof(profiles));
        }

        if (profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count)
        {
            throw new ArgumentException("Profile identifiers must be unique.", nameof(profiles));
        }

        if (profiles.Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profiles.Count)
        {
            throw new ArgumentException("Profile names must be unique.", nameof(profiles));
        }

        if (activeProfileId == Guid.Empty || profiles.All(profile => profile.Id != activeProfileId))
        {
            throw new ArgumentException("The active profile must exist.", nameof(activeProfileId));
        }

        SchemaVersion = CurrentSchemaVersion;
        ActiveProfileId = activeProfileId;
        Profiles = profiles.ToArray();
    }

    public int SchemaVersion { get; }

    public Guid ActiveProfileId { get; }

    public IReadOnlyList<ManagedNutServerProfile> Profiles { get; }

    public ManagedNutServerProfile ActiveProfile => Profiles.Single(profile => profile.Id == ActiveProfileId);

    public static ManagedNutServerProfiles CreateLegacyProfile(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var profile = new ManagedNutServerProfile(
            Guid.NewGuid(),
            "Perfil atual",
            new NutMonitoringProfile(settings.Host, settings.Port, settings.PreferredUpsName),
            new NutManagementProfile(NutManagementMode.Local),
            ManagedNutServerAccessMode.Manage);
        return new ManagedNutServerProfiles(CurrentSchemaVersion, profile.Id, [profile]);
    }
}

public sealed record ManagedServerCapabilities(
    bool CanMonitor,
    bool CanInspectLocalManagement,
    bool CanEditConfiguration,
    bool CanExecuteAdministrativeActions,
    bool CanRunDriverDiagnostics,
    bool IsRemoteManagementAvailable,
    bool CanUseRemoteManagement = false,
    bool CanInspectRemoteConfiguration = false,
    bool CanEditConfigurationByPolicy = false)
{
    public static ManagedServerCapabilities FromProfile(ManagedNutServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Management.Mode == NutManagementMode.Remote)
        {
            var canManageRemotely = profile.AccessMode == ManagedNutServerAccessMode.Manage;
            return new ManagedServerCapabilities(true, false, false, false, false, true, true, true, canManageRemotely);
        }

        var canManage = profile.AccessMode == ManagedNutServerAccessMode.Manage;
        return new ManagedServerCapabilities(true, true, canManage, canManage, canManage, false);
    }
}
