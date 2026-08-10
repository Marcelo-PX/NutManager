using CommunityToolkit.Mvvm.ComponentModel;
using NutManager.Core.Models;

namespace NutManager.App.ViewModels;

public sealed partial class ManagedNutServerProfileDraftViewModel : ObservableObject
{
    public ManagedNutServerProfileDraftViewModel(ManagedNutServerProfile profile)
    {
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

    public bool IsRemote => ManagementMode == NutManagementMode.Remote;

    public bool IsSshSftp => IsRemote && ConfigurationTransport == RemoteConfigurationTransportKind.SshSftp;

    public bool IsSmb => IsRemote && ConfigurationTransport == RemoteConfigurationTransportKind.Smb;

    public static ManagedNutServerProfileDraftViewModel CreateLocal() => new(new ManagedNutServerProfile(
        Guid.NewGuid(),
        "Novo servidor local",
        new NutMonitoringProfile("localhost"),
        new NutManagementProfile(NutManagementMode.Local),
        ManagedNutServerAccessMode.ReadOnly));

    public static ManagedNutServerProfileDraftViewModel CreateRemote()
    {
        var draft = new ManagedNutServerProfileDraftViewModel(new ManagedNutServerProfile(
            Guid.NewGuid(),
            "Novo servidor remoto",
            new NutMonitoringProfile("pendente"),
            new NutManagementProfile(NutManagementMode.Remote, "pendente"),
            ManagedNutServerAccessMode.ReadOnly));
        draft.MonitoringHost = string.Empty;
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
        TrustedHostKeyFingerprint = profile.Management.TrustedHostKeyFingerprint;
        TrustedHostKeyAlgorithm = profile.Management.TrustedHostKeyAlgorithm;
        ConfigurationTransport = profile.Management.ConfigurationTransport;
        SmbSharePath = profile.Management.SmbSharePath;
        SmbConfigurationDirectory = profile.Management.SmbConfigurationDirectory;
        SmbAuthenticationMode = profile.Management.SmbAuthenticationMode;
        SmbUsername = profile.Management.SmbUsername;
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
        TrustedHostKeyFingerprint = source.TrustedHostKeyFingerprint;
        TrustedHostKeyAlgorithm = source.TrustedHostKeyAlgorithm;
        ConfigurationTransport = source.ConfigurationTransport;
        SmbSharePath = source.SmbSharePath;
        SmbConfigurationDirectory = source.SmbConfigurationDirectory;
        SmbAuthenticationMode = source.SmbAuthenticationMode;
        SmbUsername = source.SmbUsername;
    }

    public ManagedNutServerProfile CreateProfile() => new(
        Id,
        Name,
        new NutMonitoringProfile(
            MonitoringHost,
            int.Parse(MonitoringPort, System.Globalization.CultureInfo.InvariantCulture),
            PreferredUpsName),
        new NutManagementProfile(
            ManagementMode,
            ManagementHost,
            RemoteConfigurationDirectory,
            int.Parse(SshPort, System.Globalization.CultureInfo.InvariantCulture),
            SshUsername,
            TrustedHostKeyFingerprint,
            TrustedHostKeyAlgorithm,
            ConfigurationTransport,
            SmbSharePath,
            SmbConfigurationDirectory,
            SmbAuthenticationMode,
            SmbUsername),
        AccessMode);

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
               string.Equals(TrustedHostKeyFingerprint, profile.Management.TrustedHostKeyFingerprint, StringComparison.Ordinal) &&
               string.Equals(TrustedHostKeyAlgorithm, profile.Management.TrustedHostKeyAlgorithm, StringComparison.Ordinal) &&
               ConfigurationTransport == profile.Management.ConfigurationTransport &&
               string.Equals(SmbSharePath, profile.Management.SmbSharePath, StringComparison.Ordinal) &&
               string.Equals(SmbConfigurationDirectory, profile.Management.SmbConfigurationDirectory, StringComparison.Ordinal) &&
               SmbAuthenticationMode == profile.Management.SmbAuthenticationMode &&
               string.Equals(SmbUsername, profile.Management.SmbUsername, StringComparison.Ordinal);
    }

    partial void OnManagementModeChanged(NutManagementMode value)
    {
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(IsSshSftp));
        OnPropertyChanged(nameof(IsSmb));
    }

    partial void OnConfigurationTransportChanged(RemoteConfigurationTransportKind value)
    {
        OnPropertyChanged(nameof(IsSshSftp));
        OnPropertyChanged(nameof(IsSmb));
    }
}
