using NutManager.Core.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using NutManager.Core.Agent;
using NutManager.Core.Models;
using NutManager.Core.Validation;

namespace NutManager.App.ViewModels;

public sealed partial class ManagedNutServerProfileDraftViewModel : ObservableObject
{
    public ManagedNutServerProfileDraftViewModel(ManagedNutServerProfile profile)
    {
        ManagedFileToggles = [.. ManagedNutConfigurationFiles.SupportedKinds.Select(kind => new ManagedNutFileToggleViewModel(kind))];
        foreach (var toggle in ManagedFileToggles)
        {
            // The computed set and the empty-state warning both depend on the toggles, and the
            // dirty check compares the set, so a flipped box has to reach the draft itself.
            toggle.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(ManagedFiles));
                OnPropertyChanged(nameof(HasNoManagedFiles));
            };
        }

        Apply(profile);
    }

    public Guid Id { get; private set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _monitoringHost = string.Empty;

    [ObservableProperty]
    private string _monitoringPort = NutEndpoint.DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string? _preferredUpsName;

    [ObservableProperty]
    private NutManagementMode _managementMode;

    [ObservableProperty]
    private ManagedNutServerAccessMode _accessMode;

    [ObservableProperty]
    private string? _managementHost;

    [ObservableProperty]
    private string? _remoteConfigurationDirectory;

    [ObservableProperty]
    private string _sshPort = NutManagementProfile.DefaultSshPort.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string? _sshUsername;

    [ObservableProperty]
    private SshAuthenticationMode _sshAuthenticationMode = SshAuthenticationMode.Password;

    [ObservableProperty]
    private string? _sshPrivateKeyPath;

    [ObservableProperty]
    private string? _trustedHostKeyFingerprint;

    [ObservableProperty]
    private string? _trustedHostKeyAlgorithm;

    [ObservableProperty]
    private RemoteConfigurationTransportKind _configurationTransport = RemoteConfigurationTransportKind.SshSftp;

    [ObservableProperty]
    private string? _smbSharePath;

    [ObservableProperty]
    private string? _smbConfigurationDirectory;

    [ObservableProperty]
    private SmbAuthenticationMode _smbAuthenticationMode = SmbAuthenticationMode.CurrentWindowsIdentity;

    [ObservableProperty]
    private string? _smbUsername;

    // ==================== Windows agent ====================

    /// <summary>How this profile reaches the agent. Independent of the configuration transport.</summary>
    [ObservableProperty]
    private NutAgentTransportKind _agentTransport = NutAgentTransportKind.NamedPipe;

    [ObservableProperty]
    private string? _agentHttpsEndpoint;

    [ObservableProperty]
    private NutAgentAuthenticationMode _agentAuthentication = NutAgentAuthenticationMode.CurrentWindowsIdentity;

    /// <summary>The account name only. The password never reaches this draft.</summary>
    [ObservableProperty]
    private string? _agentUsername;

    public bool IsRemote => ManagementMode == NutManagementMode.Remote;

    public bool IsSshSftp => IsRemote && ConfigurationTransport == RemoteConfigurationTransportKind.SshSftp;

    public bool IsSmb => IsRemote && ConfigurationTransport == RemoteConfigurationTransportKind.Smb;

    public bool UsesSmbExplicitCredentials => IsSmb && SmbAuthenticationMode == SmbAuthenticationMode.ExplicitCredentials;

    /// <summary>
    /// A profile saved before the share became the exact configuration location still carries a
    /// separate directory. It is neither dropped nor silently retargeted: the value stays in the
    /// draft and the form asks the administrator to point the share at the right place, so the
    /// effective location never changes behind their back.
    /// </summary>
    public bool HasLegacySmbConfigurationDirectory => IsSmb &&
        !string.IsNullOrWhiteSpace(SmbConfigurationDirectory) &&
        !string.Equals(
            SmbConfigurationDirectory?.TrimEnd('\\'),
            SmbSharePath?.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    public bool IsSshPrivateKey => IsSshSftp && SshAuthenticationMode == SshAuthenticationMode.PrivateKey;

    // ==================== Windows agent derived state ====================

    /// <summary>There is no remote agent to reach from a local profile.</summary>
    public bool IsAgentSectionVisible => IsRemote;

    public bool IsAgentNamedPipe => IsRemote && AgentTransport == NutAgentTransportKind.NamedPipe;

    public bool IsAgentHttps => IsRemote && AgentTransport == NutAgentTransportKind.Https;

    /// <summary>
    /// Only HTTPS can carry an explicit credential. Over the named pipe the caller is whoever
    /// Windows already authenticated, so the alternate account is not offered rather than offered
    /// and quietly ignored.
    /// </summary>
    public bool UsesAgentAlternateAccount =>
        IsAgentHttps && AgentAuthentication == NutAgentAuthenticationMode.AlternateWindowsAccount;

    public bool HasInvalidAgentHttpsEndpoint =>
        IsAgentHttps && !NutAgentProfileSettings.IsValidHttpsEndpoint(AgentHttpsEndpoint);

    // ==================== Managed NUT files ====================

    /// <summary>
    /// One toggle per supported file, in the fixed presentation order. The draft holds the toggles
    /// rather than a set so the checkboxes bind directly and dirty tracking works the same way it
    /// does for every other field.
    /// </summary>
    public IReadOnlyList<ManagedNutFileToggleViewModel> ManagedFileToggles { get; }

    public ManagedNutConfigurationFiles ManagedFiles =>
        ManagedNutConfigurationFiles.Create(ManagedFileToggles.Where(toggle => toggle.IsEnabled).Select(toggle => toggle.Kind));

    public bool HasNoManagedFiles => ManagedFiles.IsEmpty;

    /// <summary>Replaces every toggle at once, used by Apply and by an explicit detection result.</summary>
    public void SetManagedFiles(ManagedNutConfigurationFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach (var toggle in ManagedFileToggles)
        {
            toggle.IsEnabled = files.Contains(toggle.Kind);
        }
    }

    public static ManagedNutServerProfileDraftViewModel CreateNew()
    {
        var draft = new ManagedNutServerProfileDraftViewModel(new ManagedNutServerProfile(
            Guid.NewGuid(),
            "Novo servidor",
            new NutMonitoringProfile("localhost"),
            new NutManagementProfile(NutManagementMode.Local),
            ManagedNutServerAccessMode.ReadOnly));
        draft.Name = string.Empty;
        draft.ManagementMode = NutManagementMode.Local;
        draft.MonitoringHost = "localhost";
        draft.ManagementHost = string.Empty;
        draft.SshUsername = null;
        draft.TrustedHostKeyFingerprint = null;
        draft.TrustedHostKeyAlgorithm = null;
        return draft;
    }

    public void Apply(ManagedNutServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Id = profile.Id;
        Name = profile.Name;
        MonitoringHost = profile.Monitoring.Host;
        MonitoringPort = profile.Monitoring.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PreferredUpsName = profile.Monitoring.PreferredUpsName;
        ManagementMode = profile.Management.Mode;
        AccessMode = profile.AccessMode;
        ManagementHost = profile.Management.ManagementHost;
        RemoteConfigurationDirectory = profile.Management.RemoteConfigurationDirectory;
        SshPort = profile.Management.SshPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SshUsername = profile.Management.SshUsername;
        SshAuthenticationMode = profile.Management.SshAuthenticationMode;
        SshPrivateKeyPath = profile.Management.SshPrivateKeyPath;
        TrustedHostKeyFingerprint = profile.Management.TrustedHostKeyFingerprint;
        TrustedHostKeyAlgorithm = profile.Management.TrustedHostKeyAlgorithm;
        ConfigurationTransport = profile.Management.ConfigurationTransport;
        SmbSharePath = profile.Management.SmbSharePath;
        SmbConfigurationDirectory = profile.Management.SmbConfigurationDirectory;
        SmbAuthenticationMode = profile.Management.SmbAuthenticationMode;
        SmbUsername = profile.Management.SmbUsername;
        SetManagedFiles(profile.Management.ManagedFiles);
        AgentTransport = profile.Management.Agent.Transport;
        AgentHttpsEndpoint = profile.Management.Agent.HttpsEndpoint;
        AgentAuthentication = profile.Management.Agent.Authentication;
        AgentUsername = profile.Management.Agent.Username;
    }

    public void CopyFrom(ManagedNutServerProfileDraftViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Id = source.Id;
        Name = source.Name;
        MonitoringHost = source.MonitoringHost;
        MonitoringPort = source.MonitoringPort;
        PreferredUpsName = source.PreferredUpsName;
        ManagementMode = source.ManagementMode;
        AccessMode = source.AccessMode;
        ManagementHost = source.ManagementHost;
        RemoteConfigurationDirectory = source.RemoteConfigurationDirectory;
        SshPort = source.SshPort;
        SshUsername = source.SshUsername;
        SshAuthenticationMode = source.SshAuthenticationMode;
        SshPrivateKeyPath = source.SshPrivateKeyPath;
        TrustedHostKeyFingerprint = source.TrustedHostKeyFingerprint;
        TrustedHostKeyAlgorithm = source.TrustedHostKeyAlgorithm;
        ConfigurationTransport = source.ConfigurationTransport;
        SmbSharePath = source.SmbSharePath;
        SmbConfigurationDirectory = source.SmbConfigurationDirectory;
        SmbAuthenticationMode = source.SmbAuthenticationMode;
        SmbUsername = source.SmbUsername;
        SetManagedFiles(source.ManagedFiles);
        AgentTransport = source.AgentTransport;
        AgentHttpsEndpoint = source.AgentHttpsEndpoint;
        AgentAuthentication = source.AgentAuthentication;
        AgentUsername = source.AgentUsername;
    }

    public ManagedNutServerProfileInput ToInput() => new(
        Id,
        Name,
        MonitoringHost,
        MonitoringPort,
        PreferredUpsName,
        ManagementMode,
        AccessMode,
        ManagementHost,
        RemoteConfigurationDirectory,
        SshPort,
        SshUsername,
        SshAuthenticationMode,
        SshPrivateKeyPath,
        TrustedHostKeyFingerprint,
        TrustedHostKeyAlgorithm,
        ConfigurationTransport,
        SmbSharePath,
        SmbConfigurationDirectory,
        SmbAuthenticationMode,
        SmbUsername,
        ManagedFiles,
        AgentTransport,
        AgentHttpsEndpoint,
        AgentAuthentication,
        AgentUsername);

    public ManagedNutServerProfileValidationResult Validate(IEnumerable<ManagedNutServerProfile> existingProfiles) =>
        ManagedNutServerProfileValidator.Validate(ToInput(), existingProfiles);

    public bool Matches(ManagedNutServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Id == profile.Id &&
               string.Equals(Name, profile.Name, StringComparison.Ordinal) &&
               string.Equals(MonitoringHost, profile.Monitoring.Host, StringComparison.Ordinal) &&
               string.Equals(MonitoringPort, profile.Monitoring.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
               string.Equals(PreferredUpsName, profile.Monitoring.PreferredUpsName, StringComparison.Ordinal) &&
               ManagementMode == profile.Management.Mode &&
               AccessMode == profile.AccessMode &&
               string.Equals(ManagementHost, profile.Management.ManagementHost, StringComparison.Ordinal) &&
               string.Equals(RemoteConfigurationDirectory, profile.Management.RemoteConfigurationDirectory, StringComparison.Ordinal) &&
               string.Equals(SshPort, profile.Management.SshPort.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
               string.Equals(SshUsername, profile.Management.SshUsername, StringComparison.Ordinal) &&
               SshAuthenticationMode == profile.Management.SshAuthenticationMode &&
               string.Equals(SshPrivateKeyPath, profile.Management.SshPrivateKeyPath, StringComparison.Ordinal) &&
               string.Equals(TrustedHostKeyFingerprint, profile.Management.TrustedHostKeyFingerprint, StringComparison.Ordinal) &&
               string.Equals(TrustedHostKeyAlgorithm, profile.Management.TrustedHostKeyAlgorithm, StringComparison.Ordinal) &&
               ConfigurationTransport == profile.Management.ConfigurationTransport &&
               string.Equals(SmbSharePath, profile.Management.SmbSharePath, StringComparison.Ordinal) &&
               string.Equals(SmbConfigurationDirectory, profile.Management.SmbConfigurationDirectory, StringComparison.Ordinal) &&
               SmbAuthenticationMode == profile.Management.SmbAuthenticationMode &&
               string.Equals(SmbUsername, profile.Management.SmbUsername, StringComparison.Ordinal) &&
               ManagedFiles.Equals(profile.Management.ManagedFiles) &&
               AgentTransport == profile.Management.Agent.Transport &&
               string.Equals(AgentHttpsEndpoint, profile.Management.Agent.HttpsEndpoint, StringComparison.Ordinal) &&
               AgentAuthentication == profile.Management.Agent.Authentication &&
               string.Equals(AgentUsername, profile.Management.Agent.Username, StringComparison.Ordinal);
    }

    partial void OnManagementModeChanged(NutManagementMode value)
    {
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(IsSshSftp));
        OnPropertyChanged(nameof(IsSmb));
        OnPropertyChanged(nameof(IsSshPrivateKey));
        NotifySmbDerivedState();
        NotifyAgentDerivedState();
    }

    partial void OnSmbAuthenticationModeChanged(SmbAuthenticationMode value) => NotifySmbDerivedState();

    partial void OnAgentTransportChanged(NutAgentTransportKind value) => NotifyAgentDerivedState();

    partial void OnAgentAuthenticationChanged(NutAgentAuthenticationMode value) => NotifyAgentDerivedState();

    partial void OnAgentHttpsEndpointChanged(string? value) =>
        OnPropertyChanged(nameof(HasInvalidAgentHttpsEndpoint));

    private void NotifyAgentDerivedState()
    {
        OnPropertyChanged(nameof(IsAgentSectionVisible));
        OnPropertyChanged(nameof(IsAgentNamedPipe));
        OnPropertyChanged(nameof(IsAgentHttps));
        OnPropertyChanged(nameof(UsesAgentAlternateAccount));
        OnPropertyChanged(nameof(HasInvalidAgentHttpsEndpoint));
    }

    partial void OnSmbSharePathChanged(string? value) =>
        OnPropertyChanged(nameof(HasLegacySmbConfigurationDirectory));

    partial void OnSmbConfigurationDirectoryChanged(string? value) =>
        OnPropertyChanged(nameof(HasLegacySmbConfigurationDirectory));

    private void NotifySmbDerivedState()
    {
        OnPropertyChanged(nameof(UsesSmbExplicitCredentials));
        OnPropertyChanged(nameof(HasLegacySmbConfigurationDirectory));
    }

    partial void OnConfigurationTransportChanged(RemoteConfigurationTransportKind value)
    {
        OnPropertyChanged(nameof(IsSshSftp));
        OnPropertyChanged(nameof(IsSmb));
        OnPropertyChanged(nameof(IsSshPrivateKey));
        NotifySmbDerivedState();
    }

    partial void OnSshAuthenticationModeChanged(SshAuthenticationMode value)
    {
        OnPropertyChanged(nameof(IsSshPrivateKey));
    }
}

/// <summary>
/// A single file's checkbox. It carries the invariant NUT file name, which is never localized.
/// </summary>
public sealed partial class ManagedNutFileToggleViewModel : ObservableObject
{
    public ManagedNutFileToggleViewModel(NutConfigurationFileKind kind)
    {
        Kind = kind;
        FileName = ManagedNutConfigurationFiles.FileNameFor(kind);
    }

    public NutConfigurationFileKind Kind { get; }

    public string FileName { get; }

    [ObservableProperty]
    private bool _isEnabled = true;
}
