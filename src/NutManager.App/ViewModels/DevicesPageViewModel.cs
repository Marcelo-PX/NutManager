using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.App.Localization;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Core.Status;

namespace NutManager.App.ViewModels;

public sealed partial class DevicesPageViewModel : PageViewModel, IDisposable
{
    private readonly INutClient? _nutClient;
    private readonly NutEndpoint? _endpoint;
    private readonly IUpsPollingCoordinator? _polling;
    private CancellationTokenSource? _detailsCancellation;
    private int _detailsGeneration;

    public DevicesPageViewModel()
        : this(UiLanguagePreference.PtBr)
    {
    }

    public DevicesPageViewModel(UiLanguagePreference language)
        : base(new NutManagerLocalizer(language).Get("Devices.Title"), new NutManagerLocalizer(language).Get("Devices.Description"))
    {
        Strings = new NutManagerLocalizer(language);
        _devices = Array.Empty<UpsIdentity>();
        _rawVariables = Array.Empty<RawVariableViewModel>();
    }

    public DevicesPageViewModel(INutClient nutClient, NutEndpoint endpoint, string? preferredUpsName = null, UiLanguagePreference language = UiLanguagePreference.PtBr)
        : this(language)
    {
        ArgumentNullException.ThrowIfNull(nutClient);
        ArgumentNullException.ThrowIfNull(endpoint);

        _nutClient = nutClient;
        _endpoint = endpoint;
        PreferredUpsName = preferredUpsName;
    }

    public DevicesPageViewModel(INutClient nutClient, NutEndpoint endpoint, IUpsPollingCoordinator polling, string? preferredUpsName = null, UiLanguagePreference language = UiLanguagePreference.PtBr)
        : this(nutClient, endpoint, preferredUpsName, language)
    {
        _polling = polling;
        polling.StateChanged += ApplyPollingState;
    }

    public NutManagerLocalizer Strings { get; }

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

    /// <summary>Drives the compact device picker, which is pointless with a single UPS.</summary>
    public bool HasMultipleDevices => Devices.Count > 1;

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool HasNoSelectedDevice => !HasSelectedDevice;

    public bool HasRawVariables => RawVariables.Count > 0;

    public bool HasNoRawVariables => !HasRawVariables;

    public bool CanRefresh => !IsDiscovering;

    public bool HasDiscoveryError => !string.IsNullOrWhiteSpace(DiscoveryError);

    public bool HasDetailsError => !string.IsNullOrWhiteSpace(DetailsError);

    public bool IsSimulated => SelectedSnapshot?.Source == DataSource.Simulated;

    public string SelectedDeviceName => SelectedSnapshot?.Identity.Name ?? SelectedDevice?.Name ?? Strings.Get("Status.Unavailable");

    public string SelectedDeviceDescription => SelectedSnapshot?.Identity.Description ?? Strings.Get("Status.Unavailable");

    /// <summary>
    /// True only when NUT actually reported a description. The card subtitle uses this so an
    /// absent description is simply omitted instead of printing "unavailable" next to a status
    /// badge that says the device is online, which reads as a contradiction.
    /// </summary>
    public bool HasSelectedDeviceDescription => !string.IsNullOrWhiteSpace(SelectedSnapshot?.Identity.Description);

    public string SelectedDeviceManufacturer => SelectedSnapshot?.Identity.Manufacturer ?? Strings.Get("Status.Unavailable");

    public string SelectedDeviceModel => SelectedSnapshot?.Identity.Model ?? Strings.Get("Status.Unavailable");

    public string SelectedDeviceSerialNumber => SelectedSnapshot?.Identity.SerialNumber ?? Strings.Get("Status.Unavailable");

    // ==================== Selected-device technical summary (T27A) ====================
    // Projected from the loaded snapshot only. Discovery returns identities, so driver, port and
    // protocol are known for the device actually read; they are never guessed for the others.

    private string? Variable(params string[] names)
    {
        if (SelectedSnapshot is null) return null;
        foreach (var name in names)
            if (SelectedSnapshot.Variables.TryGetValue(name, out var variable) && !string.IsNullOrWhiteSpace(variable.Value))
                return variable.Value;
        return null;
    }

    public string SelectedDeviceDriver => Variable("driver.name") ?? Strings.Get("Status.Unavailable");

    public string SelectedDevicePort => Variable("driver.parameter.port", "port") is { } port
        ? NutPortPresentation.Friendly(port)
        : Strings.Get("Status.Unavailable");

    public string SelectedDeviceProtocol => Variable("driver.parameter.protocol", "ups.firmware") ?? Strings.Get("Status.Unavailable");

    public string? SelectedDevicePower => Variable("ups.realpower.nominal", "ups.power.nominal");

    public bool HasSelectedDevicePower => SelectedDevicePower is not null;

    public string SelectedDeviceLastUpdate => SelectedSnapshot is null
        ? Strings.Get("Status.Unavailable")
        : NutTimestampPresentation.Local(SelectedSnapshot.LastSuccessfulUpdate, "T");

    private StatusSeverity? SelectedSeverity => SelectedSnapshot?.StatusTokens.Count > 0
        ? SelectedSnapshot.StatusTokens.Max(token => token.Severity)
        : null;

    public string SelectedDeviceStatusText => SelectedSnapshot?.StatusTokens.Count > 0
        ? SelectedSnapshot.StatusTokens.OrderByDescending(token => token.Severity).First().State switch
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
            _ => SelectedSnapshot.StatusTokens[0].OriginalToken
        }
        : Strings.Get("Status.Unavailable");

    public bool IsSelectedDeviceHealthy => SelectedSeverity is StatusSeverity.Normal or StatusSeverity.Informational;

    public bool IsSelectedDeviceWarning => SelectedSeverity == StatusSeverity.Warning;

    public bool IsSelectedDeviceCritical => SelectedSeverity == StatusSeverity.Critical;

    public bool IsSelectedDeviceUnknown => SelectedSeverity is null;

    // ==================== Adaptive identification (T27A Devices V2) ====================
    // device.* is preferred and ups.* is the compatibility fallback; the same concept is never
    // rendered twice. When a driver publishes none of these, the section collapses into one line
    // instead of printing a column of "unavailable" values.

    private string? Identity(string deviceKey, string upsKey) => Variable(deviceKey, upsKey);

    public string? ManufacturerValue => Identity("device.mfr", "ups.mfr") ?? SelectedSnapshot?.Identity.Manufacturer;
    public string? ModelValue => Identity("device.model", "ups.model") ?? SelectedSnapshot?.Identity.Model;
    public string? SerialValue => Identity("device.serial", "ups.serial") ?? SelectedSnapshot?.Identity.SerialNumber;
    public string? FirmwareValue => Variable("ups.firmware");
    public string? DeviceTypeValue => Variable("device.type", "ups.type");

    public bool HasManufacturer => !string.IsNullOrWhiteSpace(ManufacturerValue);
    public bool HasModel => !string.IsNullOrWhiteSpace(ModelValue);
    public bool HasSerial => !string.IsNullOrWhiteSpace(SerialValue);
    public bool HasFirmware => !string.IsNullOrWhiteSpace(FirmwareValue);
    public bool HasDeviceType => !string.IsNullOrWhiteSpace(DeviceTypeValue);

    public bool HasAnyIdentification => HasManufacturer || HasModel || HasSerial || HasFirmware || HasDeviceType;

    // ==================== Communication ====================
    public string? DriverVersionValue => Variable("driver.version.internal", "driver.version");
    public bool HasDriverVersionValue => !string.IsNullOrWhiteSpace(DriverVersionValue);
    public string? PollIntervalValue => Variable("driver.parameter.pollinterval") is { } interval ? $"{interval} s" : null;
    public bool HasPollInterval => PollIntervalValue is not null;

    // ==================== Current readings ====================
    // Each reading is rendered only when the snapshot actually carries it.
    private string? Measure(decimal? value, string unit) => value is { } reading ? $"{Number(reading)} {unit}" : null;
    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    public string? BatteryChargeValue => SelectedSnapshot?.BatteryChargePercentage is { } charge ? $"{Number(charge)}%" : null;
    public string? LoadValue => SelectedSnapshot?.LoadPercentage is { } load ? $"{Number(load)}%" : null;
    public string? InputVoltageValue => Measure(SelectedSnapshot?.InputVoltage, "V");
    public string? OutputVoltageValue => Measure(SelectedSnapshot?.OutputVoltage, "V");
    public string? FrequencyValue => Measure(SelectedSnapshot?.Frequency, "Hz");
    public string? TemperatureValue => Measure(SelectedSnapshot?.Temperature, "°C");
    public string? BatteryVoltageValue => Measure(SelectedSnapshot?.BatteryVoltage, "V");
    public string? RuntimeValue => SelectedSnapshot?.Runtime is { } runtime
        ? runtime.TotalHours >= 1
            ? $"{(int)runtime.TotalHours} h {runtime.Minutes:D2} min"
            : $"{Math.Max(0, (int)runtime.TotalMinutes)} min"
        : null;
    public string? AlarmValue => Variable("ups.alarm");

    public bool HasBatteryCharge => BatteryChargeValue is not null;
    public bool HasLoad => LoadValue is not null;
    public bool HasInputVoltage => InputVoltageValue is not null;
    public bool HasOutputVoltage => OutputVoltageValue is not null;
    public bool HasFrequencyValue => FrequencyValue is not null;
    public bool HasTemperatureValue => TemperatureValue is not null;
    public bool HasBatteryVoltageValue => BatteryVoltageValue is not null;
    public bool HasRuntimeValue => RuntimeValue is not null;
    public bool HasAlarm => !string.IsNullOrWhiteSpace(AlarmValue);

    public bool HasAnyReading => HasBatteryCharge || HasLoad || HasInputVoltage || HasOutputVoltage ||
        HasFrequencyValue || HasTemperatureValue || HasBatteryVoltageValue || HasRuntimeValue;

    /// <summary>Raw ups.status tokens, preserved verbatim and shown alongside the localized state.</summary>
    public string? StatusTokensValue => SelectedSnapshot?.StatusTokens.Count > 0
        ? string.Join(' ', SelectedSnapshot.StatusTokens.Select(token => token.OriginalToken))
        : null;

    public bool HasStatusTokens => StatusTokensValue is not null;

    private static readonly string[] AdaptiveProperties =
    [
        nameof(ManufacturerValue), nameof(ModelValue), nameof(SerialValue), nameof(FirmwareValue), nameof(DeviceTypeValue),
        nameof(HasManufacturer), nameof(HasModel), nameof(HasSerial), nameof(HasFirmware), nameof(HasDeviceType),
        nameof(HasAnyIdentification), nameof(DriverVersionValue), nameof(HasDriverVersionValue),
        nameof(PollIntervalValue), nameof(HasPollInterval),
        nameof(BatteryChargeValue), nameof(LoadValue), nameof(InputVoltageValue), nameof(OutputVoltageValue),
        nameof(FrequencyValue), nameof(TemperatureValue), nameof(BatteryVoltageValue), nameof(RuntimeValue), nameof(AlarmValue),
        nameof(HasBatteryCharge), nameof(HasLoad), nameof(HasInputVoltage), nameof(HasOutputVoltage),
        nameof(HasFrequencyValue), nameof(HasTemperatureValue), nameof(HasBatteryVoltageValue), nameof(HasRuntimeValue),
        nameof(HasAlarm), nameof(HasAnyReading), nameof(StatusTokensValue), nameof(HasStatusTokens)
    ];

    private static readonly string[] SelectedDeviceProperties =
    [
        nameof(SelectedDeviceDriver), nameof(SelectedDevicePort), nameof(SelectedDeviceProtocol),
        nameof(SelectedDevicePower), nameof(HasSelectedDevicePower), nameof(SelectedDeviceLastUpdate),
        nameof(SelectedDeviceStatusText), nameof(IsSelectedDeviceHealthy), nameof(IsSelectedDeviceWarning),
        nameof(IsSelectedDeviceCritical), nameof(IsSelectedDeviceUnknown)
    ];

    public bool IsDisconnected => _polling?.State.ConnectionState is ConnectionState.Disconnected or ConnectionState.ConnectionFailed;

    public string EmptyStateText => IsDisconnected
        ? Strings.Get("Devices.Disconnected")
        : Strings.Get("Devices.NoDevices");

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
            if (!string.Equals(SelectedSnapshot?.Identity.Name, selectedDevice?.Name, StringComparison.Ordinal)) { SelectedSnapshot = null; RawVariables = Array.Empty<RawVariableViewModel>(); }
            DetailsError = null;
            OnSelectionStateChanged();

            if (selectedDevice is not null)
            {
                if (_polling is not null)
                {
                    if (string.Equals(previousName, selectedDevice.Name, StringComparison.Ordinal)) await _polling.RefreshAsync(cancellationToken);
                    else await _polling.MonitorAsync(selectedDevice.Name, cancellationToken);
                }
                else await LoadDetailsAsync(selectedDevice, cancellationToken);
            }
            else if (_polling is not null) await _polling.MonitorAsync(null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            DiscoveryError = Strings.Get("Devices.DiscoveryError");
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
            if (_polling is not null) await _polling.MonitorAsync(device.Name, cancellationToken); else await LoadDetailsAsync(device, cancellationToken);
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
                DetailsError = Strings.Get("Devices.DetailsError");
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
        if (_polling is not null) _polling.StateChanged -= ApplyPollingState;
    }

    private void ApplyPollingState(PollingState state)
    {
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(EmptyStateText));
        if (!string.Equals(state.UpsName, SelectedDevice?.Name, StringComparison.Ordinal)) return;
        DetailsError = state.LastError;
        if (state.Snapshot is null) return;
        SelectedSnapshot = state.Snapshot;
        RawVariables = state.Snapshot.Variables.Values.OrderBy(variable => variable.Name, StringComparer.Ordinal).Select(variable => new RawVariableViewModel(variable.Name, variable.Value)).ToArray();
        OnSelectionStateChanged();
    }

    partial void OnSelectedSnapshotChanged(UpsSnapshot? value)
    {
        OnPropertyChanged(nameof(SelectedDeviceName));
        OnPropertyChanged(nameof(SelectedDeviceDescription));
        OnPropertyChanged(nameof(SelectedDeviceManufacturer));
        OnPropertyChanged(nameof(SelectedDeviceModel));
        OnPropertyChanged(nameof(SelectedDeviceSerialNumber));
        OnPropertyChanged(nameof(IsSimulated));
        OnPropertyChanged(nameof(HasSelectedDeviceDescription));
        foreach (var property in SelectedDeviceProperties) OnPropertyChanged(property);
        foreach (var property in AdaptiveProperties) OnPropertyChanged(property);
    }

    partial void OnDevicesChanged(IReadOnlyList<UpsIdentity> value)
    {
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasNoDevices));
        OnPropertyChanged(nameof(HasMultipleDevices));
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
