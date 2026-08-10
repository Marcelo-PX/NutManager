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

    public bool IsRemote => ManagementMode == NutManagementMode.Remote;

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
    }

    public ManagedNutServerProfile CreateProfile() => new(
        Id,
        Name,
        new NutMonitoringProfile(
            MonitoringHost,
            int.Parse(MonitoringPort, System.Globalization.CultureInfo.InvariantCulture),
            PreferredUpsName),
        new NutManagementProfile(ManagementMode, ManagementHost, RemoteConfigurationDirectory),
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
               string.Equals(RemoteConfigurationDirectory, profile.Management.RemoteConfigurationDirectory, StringComparison.Ordinal);
    }

    partial void OnManagementModeChanged(NutManagementMode value) => OnPropertyChanged(nameof(IsRemote));
}
