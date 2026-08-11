using System.Globalization;
using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using NutManager.App.Services;
using NutManager.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Core.Status;

namespace NutManager.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string Description { get; }
}
public sealed partial class OverviewPageViewModel : PageViewModel
{
    private readonly INutClient? _nutClient;
    private readonly NutEndpoint? _endpoint;
    private readonly string? _upsName;
    private readonly IUpsPollingCoordinator? _polling;

    public OverviewPageViewModel()
        : this(UiLanguagePreference.PtBr)
    {
    }

    public OverviewPageViewModel(UiLanguagePreference language)
        : base(new NutManagerLocalizer(language).Get("Overview.Title"), new NutManagerLocalizer(language).Get("Overview.Description"))
    {
        Strings = new NutManagerLocalizer(language);
        _connectionState = ConnectionState.Disconnected;
        _dataFreshness = DataFreshness.Unavailable;
        _metricCards = CreateMetricCards(null);
        _statusItems = Array.Empty<OverviewStatusItemViewModel>();
    }

    public OverviewPageViewModel(
        INutClient nutClient,
        NutEndpoint endpoint,
        string upsName,
        ConnectionState connectionState,
        DataFreshness dataFreshness)
        : this(nutClient, endpoint, upsName, connectionState, dataFreshness, UiLanguagePreference.PtBr)
    {
    }

    public OverviewPageViewModel(
        INutClient nutClient,
        NutEndpoint endpoint,
        string upsName,
        ConnectionState connectionState,
        DataFreshness dataFreshness,
        UiLanguagePreference language)
        : base(new NutManagerLocalizer(language).Get("Overview.Title"), new NutManagerLocalizer(language).Get("Overview.Description"))
    {
        ArgumentNullException.ThrowIfNull(nutClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(upsName);

        Strings = new NutManagerLocalizer(language);
        _nutClient = nutClient;
        _endpoint = endpoint;
        _upsName = upsName;
        _connectionState = connectionState;
        _dataFreshness = dataFreshness;
        _metricCards = CreateMetricCards(null);
        _statusItems = Array.Empty<OverviewStatusItemViewModel>();
    }

    public OverviewPageViewModel(IUpsPollingCoordinator polling, UiLanguagePreference language = UiLanguagePreference.PtBr)
        : this(language)
    {
        _polling = polling;
        polling.StateChanged += ApplyPollingState;
        ApplyPollingState(polling.State);
    }

    public NutManagerLocalizer Strings { get; }

    private void ApplyPollingState(PollingState state)
    {
        Snapshot = state.Snapshot;
        ConnectionState = state.ConnectionState;
        DataFreshness = state.DataFreshness;
        LoadError = state.LastError;
        StatusItems = state.Snapshot?.StatusTokens.Select(CreateStatusItem).ToArray() ?? Array.Empty<OverviewStatusItemViewModel>();
        MetricCards = CreateMetricCards(state.Snapshot);
    }

    [ObservableProperty]
    private UpsSnapshot? _snapshot;

    [ObservableProperty]
    private ConnectionState _connectionState;

    [ObservableProperty]
    private DataFreshness _dataFreshness;

    [ObservableProperty]
    private IReadOnlyList<OverviewMetricViewModel> _metricCards;

    [ObservableProperty]
    private IReadOnlyList<OverviewStatusItemViewModel> _statusItems;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadError;

    public UpsIdentity? Identity => Snapshot?.Identity;

    public string SourceLabel => Snapshot?.Source == DataSource.Simulated ? Strings.Get("Shell.SimulationActive") : string.Empty;

    public bool IsSimulated => Snapshot?.Source == DataSource.Simulated;

    public bool HasNoStatusItems => StatusItems.Count == 0;

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);

    public string ConnectionStateText => ConnectionState switch
    {
        ConnectionState.Disconnected => Strings.Get("Status.Disconnected"),
        ConnectionState.Connecting => Strings.Get("Status.Connecting"),
        ConnectionState.Connected => Strings.Get("Status.Connected"),
        ConnectionState.Reconnecting => Strings.Get("Status.Reconnecting"),
        ConnectionState.ConnectionFailed => Strings.Get("Status.ConnectionFailed"),
        _ => Strings.Get("Status.Unavailable")
    };

    public string DataFreshnessText => DataFreshness switch
    {
        DataFreshness.Unavailable => Strings.Get("Status.Unavailable"),
        DataFreshness.Fresh => Strings.Get("Status.Fresh"),
        DataFreshness.Stale => Strings.Get("Status.Stale"),
        _ => Strings.Get("Status.Unavailable")
    };

    public string LastSuccessfulUpdateText => Snapshot is null
        ? Strings.Get("Status.Unavailable")
        : Snapshot.LastSuccessfulUpdate.ToString("g", CultureInfo.CurrentCulture);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_nutClient is null || _endpoint is null || _upsName is null)
        {
            return;
        }

        IsLoading = true;
        LoadError = null;

        try
        {
            Snapshot = await _nutClient.GetSnapshotAsync(_endpoint, _upsName, cancellationToken);
            StatusItems = Snapshot.StatusTokens.Select(CreateStatusItem).ToArray();
            MetricCards = CreateMetricCards(Snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            LoadError = Strings.Get("Overview.LoadError");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSnapshotChanged(UpsSnapshot? value)
    {
        OnPropertyChanged(nameof(Identity));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(IsSimulated));
        OnPropertyChanged(nameof(LastSuccessfulUpdateText));
    }

    partial void OnConnectionStateChanged(ConnectionState value) =>
        OnPropertyChanged(nameof(ConnectionStateText));

    partial void OnDataFreshnessChanged(DataFreshness value) =>
        OnPropertyChanged(nameof(DataFreshnessText));

    partial void OnLoadErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasLoadError));

    partial void OnStatusItemsChanged(IReadOnlyList<OverviewStatusItemViewModel> value) =>
        OnPropertyChanged(nameof(HasNoStatusItems));

    private IReadOnlyList<OverviewMetricViewModel> CreateMetricCards(UpsSnapshot? snapshot) =>
    [
        CreateDecimalMetric(Strings.Get("Overview.Metric.BatteryCharge"), snapshot?.BatteryChargePercentage, "%"),
        CreateDurationMetric(Strings.Get("Overview.Metric.Runtime"), snapshot?.Runtime),
        CreateDecimalMetric(Strings.Get("Overview.Metric.Load"), snapshot?.LoadPercentage, "%"),
        CreateDecimalMetric(Strings.Get("Overview.Metric.InputVoltage"), snapshot?.InputVoltage, "V"),
        CreateDecimalMetric(Strings.Get("Overview.Metric.OutputVoltage"), snapshot?.OutputVoltage, "V"),
        CreateDecimalMetric(Strings.Get("Overview.Metric.Frequency"), snapshot?.Frequency, "Hz"),
        CreateDecimalMetric(Strings.Get("Overview.Metric.Temperature"), snapshot?.Temperature, "°C"),
        CreateDecimalMetric(Strings.Get("Overview.Metric.BatteryVoltage"), snapshot?.BatteryVoltage, "V")
    ];

    private OverviewMetricViewModel CreateDecimalMetric(string title, decimal? value, string unit) =>
        value is null
            ? new OverviewMetricViewModel(title, Strings.Get("Status.Unavailable"), null)
            : new OverviewMetricViewModel(title, value.Value.ToString("0.##", CultureInfo.CurrentCulture), unit);

    private OverviewMetricViewModel CreateDurationMetric(string title, TimeSpan? value) =>
        value is null
            ? new OverviewMetricViewModel(title, Strings.Get("Status.Unavailable"), null)
            : new OverviewMetricViewModel(title, FormatDuration(value.Value), null);

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours} h {value.Minutes:D2} min"
        : $"{Math.Max(0, (int)value.TotalMinutes)} min";

    private OverviewStatusItemViewModel CreateStatusItem(UpsStatusToken token) =>
        new(
            token.OriginalToken,
            token.State switch
            {
                StatusSemanticState.Online => Strings.Get("UpsStatus.Online"),
                StatusSemanticState.OnBattery => Strings.Get("UpsStatus.OnBattery"),
                StatusSemanticState.LowBattery => Strings.Get("UpsStatus.LowBattery"),
                StatusSemanticState.ReplaceBattery => Strings.Get("UpsStatus.ReplaceBattery"),
                StatusSemanticState.Charging => Strings.Get("UpsStatus.Charging"),
                StatusSemanticState.Discharging => Strings.Get("UpsStatus.Discharging"),
                StatusSemanticState.Bypass => Strings.Get("UpsStatus.Bypass"),
                StatusSemanticState.OutputOff => Strings.Get("UpsStatus.OutputOff"),
                StatusSemanticState.Overloaded => Strings.Get("UpsStatus.Overloaded"),
                StatusSemanticState.Calibration => Strings.Get("UpsStatus.Calibration"),
                _ => token.OriginalToken
            },
            token.Severity switch
            {
                StatusSeverity.Normal => Strings.Get("Severity.Normal"),
                StatusSeverity.Informational => Strings.Get("Severity.Informational"),
                StatusSeverity.Warning => Strings.Get("Severity.Warning"),
                StatusSeverity.Critical => Strings.Get("Severity.Critical"),
                _ => Strings.Get("Common.Unknown")
            });
}

public sealed partial class DiagnosticsPageViewModel : PageViewModel, IDisposable
{
    private readonly ApplicationSettings _settings;
    private readonly ApplicationRuntimeInfo _runtimeInfo;
    private readonly IUpsPollingCoordinator? _polling;
    private readonly DevicesPageViewModel? _devices;
    private readonly ILocalNutInstallationDetector? _installationDetector;
    private readonly ILocalNutVersionResolver? _versionResolver;
    private readonly ManagedNutServerRuntimeContext? _profileContext;
    private PollingState _pollingState;
    private NutInstallationInfo _localInstallation = NutInstallationInfo.NotDetected();
    private NutVersionSource _localVersionSource = NutVersionSource.Unavailable;
    private string? _diagnosticCopyStatusMessage;

    public DiagnosticsPageViewModel()
        : this(new ApplicationSettings(), new ApplicationRuntimeInfo("-", "-", "-", "-"))
    {
    }

    public DiagnosticsPageViewModel(
        ApplicationSettings settings,
        ApplicationRuntimeInfo runtimeInfo,
        IUpsPollingCoordinator? polling = null,
        DevicesPageViewModel? devices = null,
        ILocalNutInstallationDetector? installationDetector = null,
        ManagedNutServerRuntimeContext? profileContext = null,
        UiLanguagePreference language = UiLanguagePreference.PtBr,
        ILocalNutVersionResolver? versionResolver = null)
        : base(new NutManagerLocalizer(language).Get("Diagnostics.Title"), new NutManagerLocalizer(language).Get("Diagnostics.Description"))
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runtimeInfo);

        _settings = settings;
        _runtimeInfo = runtimeInfo;
        _polling = polling;
        _devices = devices;
        _installationDetector = installationDetector;
        _versionResolver = versionResolver;
        _profileContext = profileContext;
        Strings = new NutManagerLocalizer(language);
        _pollingState = polling?.State ?? PollingState.Unavailable;

        if (_polling is not null)
        {
            _polling.StateChanged += OnPollingStateChanged;
        }

        if (_devices is not null)
        {
            _devices.PropertyChanged += OnDevicesPropertyChanged;
        }
    }

    public NutManagerLocalizer Strings { get; }

    public IReadOnlyList<string> DiagnosticGroups =>
    [
        Strings.Get("Diagnostics.Group.Overview"),
        Strings.Get("Diagnostics.Group.Connection"),
        Strings.Get("Diagnostics.Group.Polling"),
        Strings.Get("Diagnostics.Group.Discovery"),
        Strings.Get("Diagnostics.Group.Environment"),
        Strings.Get("Diagnostics.Group.Technical")
    ];

    public string? DiagnosticCopyStatusMessage
    {
        get => _diagnosticCopyStatusMessage;
        private set
        {
            if (SetProperty(ref _diagnosticCopyStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasDiagnosticCopyStatusMessage));
            }
        }
    }

    public bool HasDiagnosticCopyStatusMessage => !string.IsNullOrWhiteSpace(DiagnosticCopyStatusMessage);

    public string ApplicationName => "NutManager";
    public string ApplicationVersion => _runtimeInfo.Version;
    public string Runtime => _runtimeInfo.Runtime;
    public string OperatingSystem => _runtimeInfo.OperatingSystem;
    public string Architecture => _runtimeInfo.Architecture;

    public string ModeText => _settings.MockMode ? Strings.Get("Shell.SimulationActive") : Strings.Get("Diagnostics.LiveServer");
    public string Host => _profileContext?.Endpoint.Host ?? Strings.Get("Status.Unavailable");
    public string Port => _profileContext?.Endpoint.Port.ToString(CultureInfo.InvariantCulture) ?? Strings.Get("Status.Unavailable");
    public string ConnectionTimeoutText => FormatDuration(_settings.ConnectionTimeout);
    public string PollingIntervalText => FormatDuration(_settings.PollingInterval);
    public string PreferredUpsName => _profileContext?.Profile.Monitoring.PreferredUpsName ?? Strings.Get("Common.NotConfigured");
    public string ManagedProfileName => _profileContext?.Profile.Name ?? Strings.Get("Diagnostics.CurrentLocalProfile");
    public string ManagementModeText => _profileContext?.Profile.Management.Mode == NutManagementMode.Remote ? Strings.Get("Management.Remote") : Strings.Get("Management.Local");
    public string ManagementAccessText => _profileContext?.Profile.AccessMode == ManagedNutServerAccessMode.ReadOnly ? Strings.Get("Access.ReadOnly") : Strings.Get("Diagnostics.AccessManage");
    public bool IsLocalManagementProfile => _profileContext?.Profile.Management.Mode != NutManagementMode.Remote;

    public int DiscoveredUpsCount => _devices?.Devices.Count ?? 0;
    public string SelectedUpsName => _devices?.SelectedDevice?.Name ?? _pollingState.UpsName ?? Strings.Get("Diagnostics.NoUpsSelected");
    public string SelectedUpsDescription => DisplayIdentity?.Description ?? Strings.Get("Status.Unavailable");
    public string Manufacturer => DisplayIdentity?.Manufacturer ?? Strings.Get("Status.Unavailable");
    public string Model => DisplayIdentity?.Model ?? Strings.Get("Status.Unavailable");
    public string SerialNumber => DisplayIdentity?.SerialNumber ?? Strings.Get("Status.Unavailable");

    public string ConnectionStateText => ToConnectionStateText(_pollingState.ConnectionState);
    public string DataFreshnessText => ToDataFreshnessText(_pollingState.DataFreshness);
    public string SnapshotStatusText => _pollingState.Snapshot is null ? Strings.Get("Diagnostics.SnapshotUnavailable") : Strings.Get("Diagnostics.SnapshotAvailable");
    public string DataSourceText => _pollingState.Snapshot?.Source switch
    {
        DataSource.Simulated => Strings.Get("Shell.SimulationActive"),
        DataSource.Live => Strings.Get("Diagnostics.DataSource.NutServer"),
        _ => Strings.Get("Status.Unavailable")
    };
    public string LastSuccessfulUpdateText => _pollingState.Snapshot is null
        ? Strings.Get("Status.Unavailable")
        : _pollingState.Snapshot.LastSuccessfulUpdate.ToString("g", CultureInfo.CurrentCulture);
    public string LastErrorText => string.IsNullOrWhiteSpace(_pollingState.LastError) ? Strings.Get("Diagnostics.NoError") : _pollingState.LastError;

    public string LocalInstallationStatusText => _localInstallation.IsDetected
        ? Strings.Get("Diagnostics.InstallationFound")
        : Strings.Get("Diagnostics.InstallationNotFound");
    public string InstallationDirectoryText => _localInstallation.InstallationDirectory ?? Strings.Get("Status.Unavailable");
    public string ConfigurationDirectoryText => _localInstallation.ConfigurationDirectory ?? Strings.Get("Status.Unavailable");
    public string LocalInstallationVersionText => _localInstallation.Version ?? Strings.Get("Status.Unavailable");
    public string LocalVersionSourceText => _localVersionSource switch
    {
        NutVersionSource.FileMetadata => Strings.Get("Diagnostics.VersionSource.Metadata"),
        NutVersionSource.ExecutableFallback => Strings.Get("Diagnostics.VersionSource.Fallback"),
        _ => Strings.Get("Status.Unavailable")
    };
    public string DetectionSourceText => _localInstallation.DetectionSource ?? Strings.Get("Status.Unavailable");
    public string ExecutablesText => _localInstallation.Executables.Count == 0
        ? Strings.Get("Diagnostics.NoExecutables")
        : string.Join(Environment.NewLine, _localInstallation.Executables.Select(entry => $"{entry.Key}: {entry.Value}"));
    public string ConfigurationFilesText => _localInstallation.ConfigurationFiles.Count == 0
        ? Strings.Get("Diagnostics.NoFiles")
        : string.Join(Environment.NewLine, _localInstallation.ConfigurationFiles.Select(file =>
            $"{file.Name}: {(file.Exists ? (file.IsReadable ? Strings.Get("Diagnostics.FileAvailable") : Strings.Get("Diagnostics.FileUnreadable")) : Strings.Get("Diagnostics.FileMissing"))}"));

    public string CreateDiagnosticReport()
    {
        var lines = new[]
        {
            Strings.Get("Diagnostics.Report.Title"),
            ReportLine("Diagnostics.Report.ApplicationVersion", ApplicationVersion),
            ReportLine("Diagnostics.Report.Runtime", Runtime),
            ReportLine("Diagnostics.Report.OperatingSystem", OperatingSystem),
            ReportLine("Diagnostics.Report.Architecture", Architecture),
            ReportLine("Diagnostics.Report.Mode", ModeText),
            ReportLine("Diagnostics.Report.Profile", ManagedProfileName),
            ReportLine("Diagnostics.Report.MonitoringEndpoint", $"{Host}:{Port}"),
            ReportLine("Diagnostics.Report.ManagementMode", ManagementModeText),
            ReportLine("Diagnostics.Report.Access", ManagementAccessText),
            ReportLine("Diagnostics.Report.Connection", ConnectionStateText),
            ReportLine("Diagnostics.Report.Freshness", DataFreshnessText),
            ReportLine("Diagnostics.Report.Snapshot", SnapshotStatusText),
            ReportLine("Diagnostics.Report.Source", DataSourceText),
            ReportLine("Diagnostics.Report.DiscoveredUps", DiscoveredUpsCount.ToString(CultureInfo.InvariantCulture)),
            ReportLine("Diagnostics.Report.SelectedUps", SelectedUpsName),
            ReportLine("Diagnostics.Report.LocalInstallation", LocalInstallationStatusText),
            ReportLine("Diagnostics.Report.NutVersion", LocalInstallationVersionText),
            ReportLine("Diagnostics.Report.DetectionSource", DetectionSourceText),
            ReportLine("Diagnostics.Report.ErrorState", string.IsNullOrWhiteSpace(_pollingState.LastError)
                ? Strings.Get("Diagnostics.Report.None")
                : Strings.Get("Diagnostics.Report.PresentRedacted")),
        };
        return string.Join("\n", lines);
    }

    public void ReportDiagnosticCopyResult(bool succeeded) =>
        DiagnosticCopyStatusMessage = Strings.Get(succeeded ? "Diagnostics.Copied" : "Diagnostics.CopyFailed");
    public string? LocalInstallationError { get; private set; }
    public bool HasLocalInstallationError => !string.IsNullOrWhiteSpace(LocalInstallationError);
    public bool IsDetectingLocalInstallation { get; private set; }
    public bool CanInspectLocalInstallation =>
        IsLocalManagementProfile &&
        _installationDetector is not null &&
        !IsDetectingLocalInstallation;

    [RelayCommand]
    private Task DetectLocalInstallationAsync() => RefreshLocalInstallationAsync(CancellationToken.None);

    public async Task RefreshLocalInstallationAsync(CancellationToken cancellationToken = default)
    {
        if (_profileContext?.Profile.Management.Mode == NutManagementMode.Remote)
        {
            ApplyLocalInstallation(NutInstallationInfo.NotDetected());
            LocalInstallationError = Strings.Get("Diagnostics.RemoteNoLocalDetection");
            NotifyLocalInstallationPropertiesChanged();
            return;
        }

        if (_installationDetector is null)
        {
            ApplyLocalInstallation(NutInstallationInfo.NotDetected());
            LocalInstallationError = Strings.Get("Diagnostics.DetectionUnavailable");
            NotifyLocalInstallationPropertiesChanged();
            return;
        }

        await InspectLocalInstallationAsync(
            token => _installationDetector.DetectAsync(token),
            cancellationToken);
    }

    public Task InspectLocalInstallationDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (_profileContext?.Profile.Management.Mode == NutManagementMode.Remote)
        {
            ApplyLocalInstallation(NutInstallationInfo.NotDetected());
            LocalInstallationError = Strings.Get("Diagnostics.RemoteNoLocalInspection");
            NotifyLocalInstallationPropertiesChanged();
            return Task.CompletedTask;
        }

        if (_installationDetector is null)
        {
            ApplyLocalInstallation(NutInstallationInfo.NotDetected());
            LocalInstallationError = Strings.Get("Diagnostics.DetectionUnavailable");
            NotifyLocalInstallationPropertiesChanged();
            return Task.CompletedTask;
        }

        return InspectLocalInstallationAsync(
            token => _installationDetector.InspectDirectoryAsync(directory, token),
            cancellationToken);
    }

    public void Dispose()
    {
        if (_polling is not null)
        {
            _polling.StateChanged -= OnPollingStateChanged;
        }

        if (_devices is not null)
        {
            _devices.PropertyChanged -= OnDevicesPropertyChanged;
        }
    }

    private UpsIdentity? DisplayIdentity => _pollingState.Snapshot?.Identity ?? _devices?.SelectedDevice;

    private async Task InspectLocalInstallationAsync(
        Func<CancellationToken, Task<NutInstallationInfo>> inspectAsync,
        CancellationToken cancellationToken)
    {
        IsDetectingLocalInstallation = true;
        LocalInstallationError = null;
        NotifyLocalInstallationPropertiesChanged();
        try
        {
            var installation = await inspectAsync(cancellationToken);
            var resolution = _versionResolver is null
                ? (string.IsNullOrWhiteSpace(installation.Version)
                    ? NutVersionResolution.Unavailable
                    : new NutVersionResolution(installation.Version, NutVersionSource.FileMetadata))
                : await _versionResolver.ResolveAsync(installation, cancellationToken);
            _localVersionSource = resolution.Source;
            if (string.IsNullOrWhiteSpace(installation.Version) && !string.IsNullOrWhiteSpace(resolution.Version))
            {
                installation = installation with { Version = resolution.Version };
            }
            ApplyLocalInstallation(installation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ApplyLocalInstallation(NutInstallationInfo.NotDetected());
            LocalInstallationError = Strings.Get("Diagnostics.InspectionFailed");
        }
        finally
        {
            IsDetectingLocalInstallation = false;
            NotifyLocalInstallationPropertiesChanged();
        }
    }

    private void ApplyLocalInstallation(NutInstallationInfo installation)
    {
        if (!installation.IsDetected || string.IsNullOrWhiteSpace(installation.Version))
        {
            _localVersionSource = NutVersionSource.Unavailable;
        }
        _localInstallation = installation;
        LocalInstallationError = installation.ErrorMessage;
    }

    private void OnPollingStateChanged(PollingState state) => RunOnUiThread(() =>
    {
        _pollingState = state;
        NotifyPollingPropertiesChanged();
    });

    private void OnDevicesPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(DevicesPageViewModel.Devices) or nameof(DevicesPageViewModel.SelectedDevice))
        {
            RunOnUiThread(NotifyDevicePropertiesChanged);
        }
    }

    private void NotifyPollingPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedUpsName));
        OnPropertyChanged(nameof(SelectedUpsDescription));
        OnPropertyChanged(nameof(Manufacturer));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(SerialNumber));
        OnPropertyChanged(nameof(ConnectionStateText));
        OnPropertyChanged(nameof(DataFreshnessText));
        OnPropertyChanged(nameof(SnapshotStatusText));
        OnPropertyChanged(nameof(DataSourceText));
        OnPropertyChanged(nameof(LastSuccessfulUpdateText));
        OnPropertyChanged(nameof(LastErrorText));
    }

    private void NotifyDevicePropertiesChanged()
    {
        OnPropertyChanged(nameof(DiscoveredUpsCount));
        OnPropertyChanged(nameof(SelectedUpsName));
        OnPropertyChanged(nameof(SelectedUpsDescription));
        OnPropertyChanged(nameof(Manufacturer));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(SerialNumber));
    }

    private void NotifyLocalInstallationPropertiesChanged()
    {
        OnPropertyChanged(nameof(LocalInstallationStatusText));
        OnPropertyChanged(nameof(InstallationDirectoryText));
        OnPropertyChanged(nameof(ConfigurationDirectoryText));
        OnPropertyChanged(nameof(LocalInstallationVersionText));
        OnPropertyChanged(nameof(LocalVersionSourceText));
        OnPropertyChanged(nameof(DetectionSourceText));
        OnPropertyChanged(nameof(ExecutablesText));
        OnPropertyChanged(nameof(ConfigurationFilesText));
        OnPropertyChanged(nameof(LocalInstallationError));
        OnPropertyChanged(nameof(HasLocalInstallationError));
        OnPropertyChanged(nameof(IsDetectingLocalInstallation));
        OnPropertyChanged(nameof(CanInspectLocalInstallation));
    }

    private static void RunOnUiThread(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private static string FormatDuration(TimeSpan value) => value.TotalSeconds % 1 == 0
        ? $"{value.TotalSeconds:0} s"
        : value.ToString("c", CultureInfo.InvariantCulture);

    private string ReportLine(string labelKey, string value) => $"{Strings.Get(labelKey)}: {value}";

    public string ToConnectionStateText(ConnectionState state) => state switch
    {
        ConnectionState.Disconnected => Strings.Get("Status.Disconnected"),
        ConnectionState.Connecting => Strings.Get("Status.Connecting"),
        ConnectionState.Connected => Strings.Get("Status.Connected"),
        ConnectionState.Reconnecting => Strings.Get("Status.Reconnecting"),
        ConnectionState.ConnectionFailed => Strings.Get("Status.ConnectionFailed"),
        _ => Strings.Get("Status.Unavailable")
    };

    public string ToDataFreshnessText(DataFreshness freshness) => freshness switch
    {
        DataFreshness.Unavailable => Strings.Get("Status.Unavailable"),
        DataFreshness.Fresh => Strings.Get("Status.Fresh"),
        DataFreshness.Stale => Strings.Get("Status.Stale"),
        _ => Strings.Get("Status.Unavailable")
    };
}
