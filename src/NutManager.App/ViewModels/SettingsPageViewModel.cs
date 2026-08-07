using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.ViewModels;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IApplicationSettingsStore? _store;
    private ApplicationSettings _confirmedSettings;
    private bool _canPersistThemeAutomatically = true;

    public SettingsPageViewModel() : this(new ApplicationSettings(), null) { }

    public SettingsPageViewModel(ApplicationSettings settings, IApplicationSettingsStore? store)
        : base("Configurações", "Defina a conexão e as preferências locais do aplicativo.")
    {
        _store = store;
        _confirmedSettings = settings;
        Apply(settings);
        ThemeOptions = [new(ThemePreference.System, "Seguir o sistema"), new(ThemePreference.Light, "Claro"), new(ThemePreference.Dark, "Escuro")];
        SelectedThemeOption = ThemeOptions.Single(x => x.Preference == settings.Theme);
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    [ObservableProperty] private string _host = "localhost";
    [ObservableProperty] private string _port = "3493";
    [ObservableProperty] private string? _preferredUpsName;
    [ObservableProperty] private string _pollingIntervalSeconds = "5";
    [ObservableProperty] private string _connectionTimeoutSeconds = "5";
    [ObservableProperty] private bool _mockMode = true;
    [ObservableProperty] private ThemeOption? _selectedThemeOption;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private bool _isSaved;

    public event Action<ThemePreference>? ThemeChanged;

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        IsSaving = true; IsSaved = false; SaveError = null;
        try
        {
            var settings = CreateSettings();
            if (_store is not null) await _store.SaveAsync(settings, cancellationToken);
            _confirmedSettings = settings;
            _canPersistThemeAutomatically = true;
            IsSaved = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { SaveError = "Não foi possível salvar as configurações."; }
        finally { IsSaving = false; }
    }

    public ApplicationSettings CreateSettings() => new(
        host: Host,
        port: int.Parse(Port, System.Globalization.CultureInfo.InvariantCulture),
        preferredUpsName: PreferredUpsName,
        pollingInterval: TimeSpan.FromSeconds(double.Parse(PollingIntervalSeconds, System.Globalization.CultureInfo.InvariantCulture)),
        connectionTimeout: TimeSpan.FromSeconds(double.Parse(ConnectionTimeoutSeconds, System.Globalization.CultureInfo.InvariantCulture)),
        theme: SelectedThemeOption?.Preference ?? ThemePreference.System,
        mockMode: MockMode);

    public void Apply(ApplicationSettings settings)
    {
        Host = settings.Host; Port = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PreferredUpsName = settings.PreferredUpsName; PollingIntervalSeconds = settings.PollingInterval.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ConnectionTimeoutSeconds = settings.ConnectionTimeout.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); MockMode = settings.MockMode;
    }

    public async Task PersistThemeAsync(ThemePreference theme, CancellationToken cancellationToken = default)
    {
        if (_store is null || !_canPersistThemeAutomatically)
        {
            return;
        }

        var settings = new ApplicationSettings(
            _confirmedSettings.SchemaVersion,
            _confirmedSettings.Host,
            _confirmedSettings.Port,
            _confirmedSettings.PreferredUpsName,
            _confirmedSettings.PollingInterval,
            _confirmedSettings.ConnectionTimeout,
            theme,
            _confirmedSettings.MockMode);
        try
        {
            await _store.SaveAsync(settings, cancellationToken);
            _confirmedSettings = settings;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SaveError = "Não foi possível salvar o tema.";
        }
    }

    public void SetLoadError(string message)
    {
        LoadError = message;
        _canPersistThemeAutomatically = false;
    }

    public void ApplyTheme(ThemePreference theme)
    {
        var option = ThemeOptions.Single(x => x.Preference == theme);
        if (!Equals(SelectedThemeOption, option)) SelectedThemeOption = option;
    }
    partial void OnSelectedThemeOptionChanged(ThemeOption? value) { if (value is not null) ThemeChanged?.Invoke(value.Preference); }
}
