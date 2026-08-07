using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Mock;
using NutManager.Infrastructure.NutProtocol;
using NutManager.Infrastructure.Persistence;

namespace NutManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            { DataContext = new MainWindowViewModel() };
            desktop.MainWindow.Opened += async (_, _) => await BootstrapAsync(desktop.MainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task BootstrapAsync(Window window)
    {
        var store = new JsonApplicationSettingsStore();
        ApplicationSettings settings;
        string? loadError = null;
        try { settings = await store.LoadAsync(CancellationToken.None); }
        catch (Exception) { settings = new ApplicationSettings(); loadError = "Não foi possível carregar as configurações locais."; }

        INutClient client;
        var endpoint = new NutEndpoint(settings.Host, settings.Port, settings.ConnectionTimeout);
        if (settings.MockMode)
        {
            client = new MockNutClient(MockScenario.Online, DateTimeOffset.UtcNow);
        }
        else
        {
            client = new NutTcpClient();
        }

        var overview = settings.MockMode || settings.PreferredUpsName is not null
            ? new OverviewPageViewModel(client, endpoint, settings.MockMode ? "mockups" : settings.PreferredUpsName!,
                settings.MockMode ? ConnectionState.Connected : ConnectionState.Disconnected,
                settings.MockMode ? DataFreshness.Fresh : DataFreshness.Unavailable)
            : new OverviewPageViewModel();
        var devices = new DevicesPageViewModel(client, endpoint, settings.PreferredUpsName);
        var settingsPage = new SettingsPageViewModel(settings, store);
        if (loadError is not null) settingsPage.SetLoadError(loadError);
        var viewModel = new MainWindowViewModel(settings.Theme, overview, devices, settingsPage);
        viewModel.ThemeChanged += async preference =>
        {
            ApplyTheme(preference);
            settingsPage.ApplyTheme(preference);
            try { await settingsPage.PersistThemeAsync(preference); } catch (OperationCanceledException) { }
        };
        settingsPage.ThemeChanged += viewModel.SetTheme;
        ApplyTheme(settings.Theme);
        window.DataContext = viewModel;
        await overview.InitializeAsync();
        await devices.InitializeAsync();
    }

    private void ApplyTheme(ThemePreference preference)
    {
        RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
