using System.Globalization;
using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using NutManager.App.Services;
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
    private const string UnavailableText = "Indisponível";
    private readonly INutClient? _nutClient;
    private readonly NutEndpoint? _endpoint;
    private readonly string? _upsName;
    private readonly IUpsPollingCoordinator? _polling;

    public OverviewPageViewModel()
        : base("Visão geral", "Acompanhe o estado geral do seu ambiente de energia.")
    {
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
        : base("Visão geral", "Acompanhe o estado geral do seu ambiente de energia.")
    {
        ArgumentNullException.ThrowIfNull(nutClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(upsName);

        _nutClient = nutClient;
        _endpoint = endpoint;
        _upsName = upsName;
        _connectionState = connectionState;
        _dataFreshness = dataFreshness;
        _metricCards = CreateMetricCards(null);
        _statusItems = Array.Empty<OverviewStatusItemViewModel>();
    }

    public OverviewPageViewModel(IUpsPollingCoordinator polling)
        : this()
    {
        _polling = polling;
        polling.StateChanged += ApplyPollingState;
        ApplyPollingState(polling.State);
    }

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

    public string SourceLabel => Snapshot?.Source == DataSource.Simulated ? "Dados simulados" : string.Empty;

    public bool IsSimulated => Snapshot?.Source == DataSource.Simulated;

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);

    public string ConnectionStateText => ConnectionState switch
    {
        ConnectionState.Disconnected => "Desconectado",
        ConnectionState.Connecting => "Conectando",
        ConnectionState.Connected => "Conectado",
        ConnectionState.Reconnecting => "Reconectando",
        ConnectionState.ConnectionFailed => "Falha de conexão",
        _ => "Desconhecido"
    };

    public string DataFreshnessText => DataFreshness switch
    {
        DataFreshness.Unavailable => "Indisponível",
        DataFreshness.Fresh => "Atualizado",
        DataFreshness.Stale => "Dados desatualizados",
        _ => "Desconhecido"
    };

    public string LastSuccessfulUpdateText => Snapshot is null
        ? UnavailableText
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
            LoadError = "Não foi possível carregar os dados do UPS.";
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

    private static IReadOnlyList<OverviewMetricViewModel> CreateMetricCards(UpsSnapshot? snapshot) =>
    [
        CreateDecimalMetric("Carga da bateria", snapshot?.BatteryChargePercentage, "%"),
        CreateDurationMetric("Autonomia", snapshot?.Runtime),
        CreateDecimalMetric("Carga do UPS", snapshot?.LoadPercentage, "%"),
        CreateDecimalMetric("Tensão de entrada", snapshot?.InputVoltage, "V"),
        CreateDecimalMetric("Tensão de saída", snapshot?.OutputVoltage, "V"),
        CreateDecimalMetric("Frequência", snapshot?.Frequency, "Hz"),
        CreateDecimalMetric("Temperatura", snapshot?.Temperature, "°C"),
        CreateDecimalMetric("Tensão da bateria", snapshot?.BatteryVoltage, "V")
    ];

    private static OverviewMetricViewModel CreateDecimalMetric(string title, decimal? value, string unit) =>
        value is null
            ? new OverviewMetricViewModel(title, UnavailableText, null)
            : new OverviewMetricViewModel(title, value.Value.ToString("0.##", CultureInfo.CurrentCulture), unit);

    private static OverviewMetricViewModel CreateDurationMetric(string title, TimeSpan? value) =>
        value is null
            ? new OverviewMetricViewModel(title, UnavailableText, null)
            : new OverviewMetricViewModel(title, FormatDuration(value.Value), null);

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours} h {value.Minutes:D2} min"
        : $"{Math.Max(0, (int)value.TotalMinutes)} min";

    private static OverviewStatusItemViewModel CreateStatusItem(UpsStatusToken token) =>
        new(
            token.OriginalToken,
            token.State switch
            {
                StatusSemanticState.Online => "Em rede",
                StatusSemanticState.OnBattery => "Em bateria",
                StatusSemanticState.LowBattery => "Bateria baixa",
                StatusSemanticState.ReplaceBattery => "Substituir bateria",
                StatusSemanticState.Charging => "Carregando",
                StatusSemanticState.Discharging => "Descarregando",
                StatusSemanticState.Bypass => "Bypass",
                StatusSemanticState.OutputOff => "Saída desligada",
                StatusSemanticState.Overloaded => "Sobrecarga",
                StatusSemanticState.Calibration => "Calibração",
                _ => token.OriginalToken
            },
            token.Severity switch
            {
                StatusSeverity.Normal => "Normal",
                StatusSeverity.Informational => "Informativo",
                StatusSeverity.Warning => "Aviso",
                StatusSeverity.Critical => "Crítico",
                _ => "Desconhecido"
            });
}

public sealed partial class DiagnosticsPageViewModel : PageViewModel, IDisposable
{
    private const string UnavailableText = "Indisponível";
    private const string NoSelectionText = "Nenhum UPS selecionado";
    private readonly ApplicationSettings _settings;
    private readonly ApplicationRuntimeInfo _runtimeInfo;
    private readonly IUpsPollingCoordinator? _polling;
    private readonly DevicesPageViewModel? _devices;
    private readonly ILocalNutInstallationDetector? _installationDetector;
    private PollingState _pollingState;
    private NutInstallationInfo _localInstallation = NutInstallationInfo.NotDetected();

    public DiagnosticsPageViewModel()
        : this(new ApplicationSettings(), new ApplicationRuntimeInfo(UnavailableText, UnavailableText, UnavailableText, UnavailableText))
    {
    }

    public DiagnosticsPageViewModel(
        ApplicationSettings settings,
        ApplicationRuntimeInfo runtimeInfo,
        IUpsPollingCoordinator? polling = null,
        DevicesPageViewModel? devices = null,
        ILocalNutInstallationDetector? installationDetector = null)
        : base("Diagnóstico", "Informações somente leitura para verificar o estado atual do NutManager.")
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runtimeInfo);

        _settings = settings;
        _runtimeInfo = runtimeInfo;
        _polling = polling;
        _devices = devices;
        _installationDetector = installationDetector;
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

    public string ApplicationName => "NutManager";
    public string ApplicationVersion => _runtimeInfo.Version;
    public string Runtime => _runtimeInfo.Runtime;
    public string OperatingSystem => _runtimeInfo.OperatingSystem;
    public string Architecture => _runtimeInfo.Architecture;

    public string ModeText => _settings.MockMode ? "Dados simulados" : "Servidor NUT real";
    public string Host => _settings.Host;
    public string Port => _settings.Port.ToString(CultureInfo.InvariantCulture);
    public string ConnectionTimeoutText => FormatDuration(_settings.ConnectionTimeout);
    public string PollingIntervalText => FormatDuration(_settings.PollingInterval);
    public string PreferredUpsName => _settings.PreferredUpsName ?? "Não configurado";

    public int DiscoveredUpsCount => _devices?.Devices.Count ?? 0;
    public string SelectedUpsName => _devices?.SelectedDevice?.Name ?? _pollingState.UpsName ?? NoSelectionText;
    public string SelectedUpsDescription => DisplayIdentity?.Description ?? UnavailableText;
    public string Manufacturer => DisplayIdentity?.Manufacturer ?? UnavailableText;
    public string Model => DisplayIdentity?.Model ?? UnavailableText;
    public string SerialNumber => DisplayIdentity?.SerialNumber ?? UnavailableText;

    public string ConnectionStateText => ToConnectionStateText(_pollingState.ConnectionState);
    public string DataFreshnessText => ToDataFreshnessText(_pollingState.DataFreshness);
    public string SnapshotStatusText => _pollingState.Snapshot is null ? "Sem snapshot disponível" : "Snapshot disponível";
    public string DataSourceText => _pollingState.Snapshot?.Source switch
    {
        DataSource.Simulated => "Dados simulados",
        DataSource.Live => "Servidor NUT",
        _ => UnavailableText
    };
    public string LastSuccessfulUpdateText => _pollingState.Snapshot is null
        ? UnavailableText
        : _pollingState.Snapshot.LastSuccessfulUpdate.ToString("g", CultureInfo.CurrentCulture);
    public string LastErrorText => string.IsNullOrWhiteSpace(_pollingState.LastError) ? "Nenhum erro" : _pollingState.LastError;

    public string LocalInstallationStatusText => _localInstallation.IsDetected
        ? "Instalação NUT encontrada"
        : "Nenhuma instalação NUT local encontrada";
    public string InstallationDirectoryText => _localInstallation.InstallationDirectory ?? UnavailableText;
    public string ConfigurationDirectoryText => _localInstallation.ConfigurationDirectory ?? UnavailableText;
    public string LocalInstallationVersionText => _localInstallation.Version ?? UnavailableText;
    public string DetectionSourceText => _localInstallation.DetectionSource ?? UnavailableText;
    public string ExecutablesText => _localInstallation.Executables.Count == 0
        ? "Nenhum executável encontrado"
        : string.Join(Environment.NewLine, _localInstallation.Executables.Select(entry => $"{entry.Key}: {entry.Value}"));
    public string ConfigurationFilesText => _localInstallation.ConfigurationFiles.Count == 0
        ? "Nenhum arquivo encontrado"
        : string.Join(Environment.NewLine, _localInstallation.ConfigurationFiles.Select(file =>
            $"{file.Name}: {(file.Exists ? (file.IsReadable ? "Disponível" : "Sem acesso de leitura") : "Ausente")}"));
    public string? LocalInstallationError { get; private set; }
    public bool HasLocalInstallationError => !string.IsNullOrWhiteSpace(LocalInstallationError);
    public bool IsDetectingLocalInstallation { get; private set; }

    [RelayCommand]
    private Task DetectLocalInstallationAsync() => RefreshLocalInstallationAsync(CancellationToken.None);

    public async Task RefreshLocalInstallationAsync(CancellationToken cancellationToken = default)
    {
        if (_installationDetector is null)
        {
            ApplyLocalInstallation(NutInstallationInfo.NotDetected());
            LocalInstallationError = "A detecção local não está disponível.";
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
        if (_installationDetector is null)
        {
            ApplyLocalInstallation(NutInstallationInfo.NotDetected());
            LocalInstallationError = "A detecção local não está disponível.";
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
            ApplyLocalInstallation(await inspectAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ApplyLocalInstallation(NutInstallationInfo.NotDetected());
            LocalInstallationError = "Não foi possível inspecionar a instalação local do NUT.";
        }
        finally
        {
            IsDetectingLocalInstallation = false;
            NotifyLocalInstallationPropertiesChanged();
        }
    }

    private void ApplyLocalInstallation(NutInstallationInfo installation)
    {
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
        OnPropertyChanged(nameof(DetectionSourceText));
        OnPropertyChanged(nameof(ExecutablesText));
        OnPropertyChanged(nameof(ConfigurationFilesText));
        OnPropertyChanged(nameof(LocalInstallationError));
        OnPropertyChanged(nameof(HasLocalInstallationError));
        OnPropertyChanged(nameof(IsDetectingLocalInstallation));
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

    public static string ToConnectionStateText(ConnectionState state) => state switch
    {
        ConnectionState.Disconnected => "Desconectado",
        ConnectionState.Connecting => "Conectando",
        ConnectionState.Connected => "Conectado",
        ConnectionState.Reconnecting => "Reconectando",
        ConnectionState.ConnectionFailed => "Falha de conexão",
        _ => UnavailableText
    };

    public static string ToDataFreshnessText(DataFreshness freshness) => freshness switch
    {
        DataFreshness.Unavailable => "Indisponível",
        DataFreshness.Fresh => "Atualizado",
        DataFreshness.Stale => "Dados desatualizados",
        _ => UnavailableText
    };
}
