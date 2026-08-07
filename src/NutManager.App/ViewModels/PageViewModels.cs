using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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

public sealed class DevicesPageViewModel : PageViewModel
{
    public DevicesPageViewModel()
        : base("Dispositivos", "Veja os dispositivos disponíveis quando uma conexão for configurada.")
    {
    }
}

public sealed class DiagnosticsPageViewModel : PageViewModel
{
    public DiagnosticsPageViewModel()
        : base("Diagnóstico", "Consulte informações de diagnóstico da aplicação.")
    {
    }
}

public sealed class SettingsPageViewModel : PageViewModel
{
    public SettingsPageViewModel()
        : base("Configurações", "Ajuste as preferências visuais do aplicativo.")
    {
    }
}
