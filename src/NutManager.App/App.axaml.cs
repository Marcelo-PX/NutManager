using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using NutManager.App.Services;
using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Mock;
using NutManager.Infrastructure.Configuration;
using NutManager.Infrastructure.Credentials.Windows;
using NutManager.Infrastructure.NutProtocol;
using NutManager.Infrastructure.Persistence;
using NutManager.Infrastructure.Polling;
using NutManager.Infrastructure.Platform.Windows;
using NutManager.Infrastructure.Remote.Smb;
using NutManager.Infrastructure.Remote.Ssh;

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
        var profileStore = new JsonManagedNutServerProfileStore();
        ApplicationSettings settings;
        string? loadError = null;
        try { settings = await store.LoadAsync(CancellationToken.None); }
        catch (Exception) { settings = new ApplicationSettings(); loadError = "Não foi possível carregar as configurações locais."; }

        var profileBootstrap = await new ManagedNutServerBootstrapper(profileStore).LoadAsync(settings, CancellationToken.None);
        var runtimeProfile = profileBootstrap.RuntimeContext;
        var credentialStore = new WindowsCredentialManagerRemoteCredentialStore();
        var profileMutator = new ManagedNutServerProfileUpdateService(profileStore, credentialStore);

        INutClient client;
        var endpoint = runtimeProfile.Endpoint;
        if (settings.MockMode)
        {
            client = new MockNutClient(MockScenario.Online, DateTimeOffset.UtcNow);
        }
        else
        {
            client = new NutTcpClient();
        }

        var polling = new UpsPollingCoordinator(client, endpoint, settings.PollingInterval);
        var overview = new OverviewPageViewModel(polling);
        var devices = new DevicesPageViewModel(client, endpoint, polling, runtimeProfile.Profile.Monitoring.PreferredUpsName);
        var isLocalManagement = runtimeProfile.Profile.Management.Mode == NutManagementMode.Local;
        IRemoteNutConfigurationTransport? remoteTransport = runtimeProfile.Profile.Management.ConfigurationTransport switch
        {
            RemoteConfigurationTransportKind.Smb => new WindowsSmbRemoteNutConfigurationTransport(),
            _ => new SshNetRemoteNutManagementTransport()
        };
        var remoteManagement = isLocalManagement
            ? null
            : new RemoteManagementSessionViewModel(runtimeProfile.Profile, remoteTransport, profileMutator, credentialStore);
        var installationDetector = isLocalManagement ? new WindowsNutInstallationDetector() : null;
        var diagnostics = new DiagnosticsPageViewModel(
            settings,
            ApplicationRuntimeInfo.CreateCurrent(),
            polling,
            devices,
            installationDetector,
            runtimeProfile);
        var administration = new AdministrationPageViewModel(
            installationDetector,
            isLocalManagement ? new NutConfigurationFilePipeline() : null,
            isLocalManagement ? new WindowsLocalNutAdministration() : null,
            isLocalManagement ? new WindowsNutDriverDiagnostics() : null,
            runtimeProfile,
            remoteManagement);
        var settingsPage = new SettingsPageViewModel(settings, store, profileBootstrap.Profiles, profileStore, profileMutator, credentialStore);
        window.Closed += async (_, _) =>
        {
            if (remoteManagement is not null)
            {
                await remoteManagement.DisposeAsync();
            }

            diagnostics.Dispose();
            devices.Dispose();
            polling.Dispose();
        };
        if (loadError is not null) settingsPage.SetLoadError(loadError);
        if (profileBootstrap.Warning is not null) settingsPage.SetProfileLoadError(profileBootstrap.Warning, profileBootstrap.IsProfileDocumentLoadFailure);
        var viewModel = new MainWindowViewModel(settings.Theme, overview, devices, settingsPage, diagnostics, administration);
        viewModel.ThemeChanged += async preference =>
        {
            ApplyTheme(preference);
            settingsPage.ApplyTheme(preference);
            try { await settingsPage.PersistThemeAsync(preference); } catch (OperationCanceledException) { }
        };
        settingsPage.ThemeChanged += viewModel.SetTheme;
        ApplyTheme(settings.Theme);
        window.DataContext = viewModel;
        await devices.InitializeAsync();
        await diagnostics.RefreshLocalInstallationAsync();
        await administration.InitializeAsync();
        await settingsPage.RefreshStoredCredentialStatusAsync();
        if (remoteManagement is not null)
        {
            await remoteManagement.RefreshStoredCredentialStatusAsync();
        }
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
