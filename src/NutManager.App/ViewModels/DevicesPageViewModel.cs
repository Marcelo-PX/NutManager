using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.ViewModels;

public sealed partial class DevicesPageViewModel : PageViewModel, IDisposable
{
    private const string UnavailableText = "Indisponível";
    private readonly INutClient? _nutClient;
    private readonly NutEndpoint? _endpoint;
    private CancellationTokenSource? _detailsCancellation;
    private int _detailsGeneration;

    public DevicesPageViewModel()
        : base("Dispositivos", "Selecione um UPS para consultar suas variáveis disponíveis.")
    {
        _devices = Array.Empty<UpsIdentity>();
        _rawVariables = Array.Empty<RawVariableViewModel>();
    }

    public DevicesPageViewModel(INutClient nutClient, NutEndpoint endpoint, string? preferredUpsName = null)
        : this()
    {
        ArgumentNullException.ThrowIfNull(nutClient);
        ArgumentNullException.ThrowIfNull(endpoint);

        _nutClient = nutClient;
        _endpoint = endpoint;
        PreferredUpsName = preferredUpsName;
    }

    [ObservableProperty]
    private IReadOnlyList<UpsIdentity> _devices;

    [ObservableProperty]
    private UpsIdentity? _selectedDevice;

    [ObservableProperty]
    private UpsSnapshot? _selectedSnapshot;

    [ObservableProperty]
    private IReadOnlyList<RawVariableViewModel> _rawVariables;

    [ObservableProperty]
    private bool _isDiscovering;

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    private string? _discoveryError;

    [ObservableProperty]
    private string? _detailsError;

    public string? PreferredUpsName { get; }

    public bool HasDevices => Devices.Count > 0;

    public bool HasNoDevices => !HasDevices;

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool HasNoSelectedDevice => !HasSelectedDevice;

    public bool HasRawVariables => RawVariables.Count > 0;

    public bool HasNoRawVariables => !HasRawVariables;

    public bool CanRefresh => !IsDiscovering;

    public bool HasDiscoveryError => !string.IsNullOrWhiteSpace(DiscoveryError);

    public bool HasDetailsError => !string.IsNullOrWhiteSpace(DetailsError);

    public bool IsSimulated => SelectedSnapshot?.Source == DataSource.Simulated;

    public string SelectedDeviceName => SelectedSnapshot?.Identity.Name ?? SelectedDevice?.Name ?? UnavailableText;

    public string SelectedDeviceDescription => SelectedSnapshot?.Identity.Description ?? UnavailableText;

    public string SelectedDeviceManufacturer => SelectedSnapshot?.Identity.Manufacturer ?? UnavailableText;

    public string SelectedDeviceModel => SelectedSnapshot?.Identity.Model ?? UnavailableText;

    public string SelectedDeviceSerialNumber => SelectedSnapshot?.Identity.SerialNumber ?? UnavailableText;

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_nutClient is null || _endpoint is null)
        {
            return;
        }

        IsDiscovering = true;
        DiscoveryError = null;

        try
        {
            var discoveredDevices = await _nutClient.ListUpsAsync(_endpoint, cancellationToken);
            var previousName = SelectedDevice?.Name;
            Devices = discoveredDevices.ToArray();

            var selectedDevice = previousName is null
                ? Devices.FirstOrDefault(device => string.Equals(device.Name, PreferredUpsName, StringComparison.Ordinal))
                    ?? Devices.FirstOrDefault()
                : Devices.FirstOrDefault(device => string.Equals(device.Name, previousName, StringComparison.Ordinal))
                    ?? Devices.FirstOrDefault();

            SelectedDevice = selectedDevice;
            SelectedSnapshot = null;
            RawVariables = Array.Empty<RawVariableViewModel>();
            DetailsError = null;
            OnSelectionStateChanged();

            if (selectedDevice is not null)
            {
                await LoadDetailsAsync(selectedDevice, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            DiscoveryError = "Não foi possível descobrir os UPS disponíveis.";
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    [RelayCommand]
    private async Task SelectDeviceAsync(UpsIdentity? device, CancellationToken cancellationToken = default)
    {
        SelectedDevice = device;
        SelectedSnapshot = null;
        RawVariables = Array.Empty<RawVariableViewModel>();
        DetailsError = null;
        OnSelectionStateChanged();

        if (device is not null)
        {
            await LoadDetailsAsync(device, cancellationToken);
        }
    }

    private async Task LoadDetailsAsync(UpsIdentity device, CancellationToken cancellationToken)
    {
        if (_nutClient is null || _endpoint is null)
        {
            return;
        }

        _detailsCancellation?.Cancel();
        var generation = Interlocked.Increment(ref _detailsGeneration);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _detailsCancellation = linkedCancellation;
        IsLoadingDetails = true;
        DetailsError = null;

        try
        {
            var snapshot = await _nutClient.GetSnapshotAsync(_endpoint, device.Name, linkedCancellation.Token);

            if (generation != _detailsGeneration || !string.Equals(SelectedDevice?.Name, device.Name, StringComparison.Ordinal))
            {
                return;
            }

            SelectedSnapshot = snapshot;
            RawVariables = snapshot.Variables.Values
                .OrderBy(variable => variable.Name, StringComparer.Ordinal)
                .Select(variable => new RawVariableViewModel(variable.Name, variable.Value))
                .ToArray();
            OnSelectionStateChanged();
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation == _detailsGeneration && string.Equals(SelectedDevice?.Name, device.Name, StringComparison.Ordinal))
            {
                DetailsError = "Não foi possível carregar os detalhes do UPS.";
            }
        }
        finally
        {
            if (ReferenceEquals(_detailsCancellation, linkedCancellation))
            {
                _detailsCancellation = null;
                IsLoadingDetails = false;
            }
        }
    }

    public void Dispose()
    {
        _detailsCancellation?.Cancel();
        _detailsCancellation?.Dispose();
        _detailsCancellation = null;
    }

    partial void OnDevicesChanged(IReadOnlyList<UpsIdentity> value)
    {
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasNoDevices));
    }

    partial void OnRawVariablesChanged(IReadOnlyList<RawVariableViewModel> value)
    {
        OnPropertyChanged(nameof(HasRawVariables));
        OnPropertyChanged(nameof(HasNoRawVariables));
    }

    partial void OnIsDiscoveringChanged(bool value) =>
        OnPropertyChanged(nameof(CanRefresh));

    partial void OnDiscoveryErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasDiscoveryError));

    partial void OnDetailsErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasDetailsError));

    private void OnSelectionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(HasNoSelectedDevice));
        OnPropertyChanged(nameof(IsSimulated));
        OnPropertyChanged(nameof(SelectedDeviceName));
        OnPropertyChanged(nameof(SelectedDeviceDescription));
        OnPropertyChanged(nameof(SelectedDeviceManufacturer));
        OnPropertyChanged(nameof(SelectedDeviceModel));
        OnPropertyChanged(nameof(SelectedDeviceSerialNumber));
    }
}
