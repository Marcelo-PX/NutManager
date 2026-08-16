using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using NutManager.App.Presentation.Themes;
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

        // Icon drawings come from the library, applied after the theme dictionaries are composed so
        // anything it does not supply keeps the geometry drawn for it.
        NutIconLibrary.Apply(this);
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
        var overview = new OverviewPageViewModel(polling, settings.Language, endpoint);
        var devices = new DevicesPageViewModel(client, endpoint, polling, runtimeProfile.Profile.Monitoring.PreferredUpsName, settings.Language);
        var isLocalManagement = runtimeProfile.Profile.Management.Mode == NutManagementMode.Local;
        IRemoteNutConfigurationTransport? remoteTransport = runtimeProfile.Profile.Management.ConfigurationTransport switch
        {
            RemoteConfigurationTransportKind.Smb => new WindowsSmbRemoteNutConfigurationTransport(),
            _ => new SshNetRemoteNutManagementTransport()
        };
        var remoteManagement = isLocalManagement
            ? null
            : new RemoteManagementSessionViewModel(
                runtimeProfile.Profile,
                remoteTransport,
                profileMutator,
                credentialStore,
                settings.Language,
                new WindowsCredentialPrompt());
        // The host comes from the profile's own NUT endpoint, not from the SMB share path: the share
        // is a configuration transport that may point anywhere, while the endpoint is the machine
        // whose NUT is being monitored. The probe uses the current Windows identity and no credential
        // from any store, so it is created for a remote profile regardless of how SMB is faring.
        var remoteWindowsService = isLocalManagement
            ? null
            : new RemoteWindowsServiceViewModel(
                runtimeProfile.Endpoint.Host,
                new WindowsRemoteNutServiceProbe(),
                settings.Language);
        var installationDetector = isLocalManagement ? new WindowsNutInstallationDetector() : null;
        var diagnostics = new DiagnosticsPageViewModel(
            settings,
            ApplicationRuntimeInfo.CreateCurrent(),
            polling,
            devices,
            installationDetector,
            runtimeProfile,
            settings.Language,
            isLocalManagement ? new WindowsNutVersionResolver() : null);
        var administration = new AdministrationPageViewModel(
            installationDetector,
            isLocalManagement ? new NutConfigurationFilePipeline() : null,
            isLocalManagement ? new WindowsLocalNutAdministration() : null,
            isLocalManagement ? new WindowsNutDriverDiagnostics() : null,
            runtimeProfile,
            remoteManagement,
            settings.Language,
            isLocalManagement ? new WindowsNutDriverCatalogSource() : null,
            remoteWindowsService);
        INutManagedFileDetector managedFileDetector = isLocalManagement
            ? new LocalNutManagedFileDetector(installationDetector ?? new WindowsNutInstallationDetector())
            : new RemoteNutManagedFileDetector(() => remoteManagement?.DirectoryValidation);
        var settingsPage = new SettingsPageViewModel(
            settings,
            store,
            profileBootstrap.Profiles,
            profileStore,
            profileMutator,
            credentialStore,
            new ManagedNutConnectionTester(new NutTcpClient()),
            runtimeProfile.Profile.Id,
            managedFileDetector);
        window.Closed += async (_, _) =>
        {
            if (remoteWindowsService is not null)
            {
                await remoteWindowsService.DisposeAsync();
            }

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
        var viewModel = new MainWindowViewModel(
            settings.Theme,
            overview,
            devices,
            settingsPage,
            diagnostics,
            administration,
            settings.Language,
            settings.SidebarPreference,
            settings.MockMode,
            $"{endpoint.Host}:{endpoint.Port}",
            runtimeProfile.Profile.Name,
            runtimeProfile.Profile.Management.Mode,
            runtimeProfile.Profile.AccessMode,
            runtimeProfile.Profile.Monitoring.PreferredUpsName);
        viewModel.SetTransparencyPreference(settings.BackgroundTransparency);
        administration.SemanticReviewChanged += viewModel.SetSemanticReview;
        viewModel.ThemeChanged += async preference =>
        {
            ApplyTheme(preference);
            settingsPage.ApplyTheme(preference);
            try { await settingsPage.PersistThemeAsync(preference); } catch (OperationCanceledException) { }
        };
        settingsPage.ThemeChanged += viewModel.SetTheme;
        settingsPage.SidebarPreferenceChanged += preference => viewModel.SidebarPreference = preference;
        viewModel.SidebarPreferenceChanged += settingsPage.ApplySidebarPreference;
        settingsPage.BackgroundTransparencyChanged += viewModel.SetTransparencyPreference;
        viewModel.EffectiveThemeChanged += settingsPage.ApplyTransparencyAvailability;
        settingsPage.ApplyTransparencyAvailability(viewModel.IsEffectiveDark);
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
