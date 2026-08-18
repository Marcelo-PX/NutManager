using NutManager.App.Services;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using Xunit;

namespace NutManager.Tests;

public sealed class SettingsPageViewModelTests
{
    [Fact]
    public async Task ThemePersistenceUsesOnlyConfirmedPreferences()
    {
        var saved = new List<ApplicationSettings>();
        var confirmed = new ApplicationSettings(mockMode: true, pollingInterval: TimeSpan.FromSeconds(8));
        var viewModel = new SettingsPageViewModel(confirmed, new RecordingStore(saved));
        viewModel.PollingIntervalSeconds = "99";

        await viewModel.PersistThemeAsync(ThemePreference.Dark);

        var persisted = Assert.Single(saved);
        Assert.False(persisted.MockMode);
        Assert.Equal(TimeSpan.FromSeconds(8), persisted.PollingInterval);
        Assert.Equal(ThemePreference.Dark, persisted.Theme);
        Assert.Null(persisted.LegacyMonitoringEndpoint);
    }

    [Fact]
    public async Task ExplicitSaveBecomesBaseForFutureThemePersistence()
    {
        var saved = new List<ApplicationSettings>();
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new RecordingStore(saved));
        viewModel.PollingIntervalSeconds = "11";
        await viewModel.SaveCommand.ExecuteAsync(null);
        viewModel.PollingIntervalSeconds = "99";

        await viewModel.PersistThemeAsync(ThemePreference.Light);

        Assert.Equal(TimeSpan.FromSeconds(11), saved.Last().PollingInterval);
        Assert.Equal(ThemePreference.Light, saved.Last().Theme);
    }

    [Fact]
    public async Task LoadErrorBlocksAutomaticThemePersistenceButNotExplicitSave()
    {
        var saved = new List<ApplicationSettings>();
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new RecordingStore(saved));
        viewModel.SetLoadError("invalid settings");

        await viewModel.PersistThemeAsync(ThemePreference.Dark);
        Assert.Empty(saved);

        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Single(saved);
    }

    [Fact]
    public void ThemeSynchronizationDoesNotLoop()
    {
        var page = new SettingsPageViewModel();
        var shell = new MainWindowViewModel();
        page.ThemeChanged += shell.SetTheme;
        shell.ThemeChanged += page.ApplyTheme;

        page.ApplyTheme(ThemePreference.Dark);

        Assert.Equal(ThemePreference.Dark, shell.SelectedTheme);
        Assert.Equal(ThemePreference.Dark, page.SelectedThemeOption?.Preference);
    }

    [Fact]
    public async Task GeneralSettingsRoundTripAllPreferenceValues()
    {
        var saved = new List<ApplicationSettings>();
        var source = new ApplicationSettings(
            pollingInterval: TimeSpan.FromSeconds(8),
            connectionTimeout: TimeSpan.FromSeconds(4),
            theme: ThemePreference.Light,
            mockMode: false,
            language: UiLanguagePreference.EnUs,
            sidebarPreference: SidebarPreference.Collapsed);
        var viewModel = new SettingsPageViewModel(source, new RecordingStore(saved));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(source, Assert.Single(saved));
        Assert.True(viewModel.IsSaved);
        Assert.False(viewModel.IsSaving);
    }

    [Theory]
    [InlineData("0", "5")]
    [InlineData("bad", "5")]
    [InlineData("5", "0")]
    [InlineData("5", "bad")]
    public async Task InvalidGeneralDurationsSetSaveError(string polling, string timeout)
    {
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new RecordingStore([]))
        {
            PollingIntervalSeconds = polling,
            ConnectionTimeoutSeconds = timeout
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SaveError);
        Assert.False(viewModel.IsSaved);
    }

    [Fact]
    public async Task StoreFailureIsVisible()
    {
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new FailingStore());

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SaveError);
        Assert.False(viewModel.IsSaving);
        Assert.False(viewModel.IsSaved);
    }

    [Fact]
    public async Task ProfileSaveAfterCredentialRemovalReportsPartialOutcome()
    {
        var profile = RemoteProfile();
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]);
        var profileStore = new ProfileStore(profiles) { ThrowOnSave = true };
        var credentials = new CredentialStore();
        var viewModel = CreateProfileViewModel(profiles, profileStore, credentials);
        var persistedNotifications = 0;
        viewModel.ProfilePersisted += _ => persistedNotifications++;
        viewModel.ProfileDraft.ManagementHost = "new-management.example";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsProfileSaved);
        Assert.Equal("management.example", profileStore.Current.ActiveProfile.Management.ManagementHost);
        Assert.Equal(0, persistedNotifications);
        Assert.Contains("não pôde ser salvo", viewModel.ProfileSaveError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("credenciais protegidas", viewModel.ProfileSaveError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CredentialRemovalFailureLeavesProfileUnchanged()
    {
        var profile = RemoteProfile();
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]);
        var profileStore = new ProfileStore(profiles);
        var credentials = new CredentialStore { DeleteResult = new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.AccessDenied) };
        var viewModel = CreateProfileViewModel(profiles, profileStore, credentials);
        viewModel.ProfileDraft.ManagementHost = "new-management.example";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsProfileSaved);
        Assert.Equal("management.example", profileStore.Current.ActiveProfile.Management.ManagementHost);
        Assert.Contains("não foi alterado", viewModel.ProfileSaveError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulProfileSavePublishesOnlyTheConfirmedManagedFileScope()
    {
        var profile = RemoteProfile();
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]);
        var profileStore = new ProfileStore(profiles);
        var viewModel = CreateProfileViewModel(profiles, profileStore, new CredentialStore());
        ManagedNutServerProfile? persisted = null;
        viewModel.ProfilePersisted += profile => persisted = profile;
        viewModel.ProfileDraft.ManagedFileToggles
            .Single(toggle => toggle.Kind == NutConfigurationFileKind.UpsdUsers)
            .IsEnabled = false;

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsProfileSaved);
        Assert.NotNull(persisted);
        Assert.False(persisted.Management.ManagedFiles.Contains(NutConfigurationFileKind.UpsdUsers));
        Assert.Equal(profileStore.Current.ActiveProfile, persisted);
    }

    [Fact]
    public void ExplicitSmbCredentialHelpNamesTheAdministrationPageInBothCultures()
    {
        // Settings reports whether an SMB credential is stored and offers to forget it, but signing
        // in happens on the Administration page. This text is the only thing joining the two, so it
        // has to name that page rather than merely resolve to something non-empty.
        foreach (var (language, page) in new[]
                 {
                     (UiLanguagePreference.PtBr, "Administração"),
                     (UiLanguagePreference.EnUs, "Administration")
                 })
        {
            var value = new App.Localization.NutManagerLocalizer(language).Get("Profiles.SmbSecretHelp");

            Assert.NotEqual("Profiles.SmbSecretHelp", value);
            Assert.Contains(page, value, StringComparison.Ordinal);
        }

        // "Administra" is the shared prefix of both spellings, so this stays true whichever culture
        // the default settings resolve to.
        Assert.Contains(
            "Administra",
            new SettingsPageViewModel(new ApplicationSettings(), null).SmbSecretHelp,
            StringComparison.Ordinal);
    }

    private static SettingsPageViewModel CreateProfileViewModel(
        ManagedNutServerProfiles profiles,
        ProfileStore profileStore,
        CredentialStore credentials) => new(
            new ApplicationSettings(),
            null,
            profiles,
            profileStore,
            new ManagedNutServerProfileUpdateService(profileStore, credentials),
            credentials);

    private static ManagedNutServerProfile RemoteProfile() => new(
        Guid.NewGuid(),
        "Remote",
        new NutMonitoringProfile("monitor.example"),
        new NutManagementProfile(NutManagementMode.Remote, "management.example", "/etc/nut", sshUsername: "nutadmin"),
        ManagedNutServerAccessMode.Manage);

    private sealed class RecordingStore : IApplicationSettingsStore
    {
        private readonly List<ApplicationSettings> _settings;

        public RecordingStore(List<ApplicationSettings> settings) => _settings = settings;

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new ApplicationSettings());

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            _settings.Add(settings);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingStore : IApplicationSettingsStore
    {
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) => throw new IOException();
    }

    private sealed class ProfileStore : IManagedNutServerProfileStore
    {
        public ProfileStore(ManagedNutServerProfiles current) => Current = current;

        public ManagedNutServerProfiles Current { get; private set; }

        public bool ThrowOnSave { get; set; }

        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ManagedNutServerProfiles?>(Current);

        public Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken)
        {
            if (ThrowOnSave)
            {
                throw new IOException("Simulated profile persistence failure.");
            }

            Current = profiles;
            return Task.CompletedTask;
        }
    }

    private sealed class CredentialStore : IRemoteCredentialStore
    {
        public RemoteCredentialStoreResult DeleteResult { get; set; } = new(RemoteCredentialStoreStatus.Success);

        public Task<RemoteCredentialStoreResult> ContainsAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.NotFound));

        public Task<RemoteCredentialReadResult> ReadAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteCredentialReadResult(RemoteCredentialStoreStatus.NotFound));

        public Task<RemoteCredentialStoreResult> WriteAsync(Guid profileId, RemoteCredentialKind kind, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success));

        public Task<RemoteCredentialStoreResult> DeleteAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default) => Task.FromResult(DeleteResult);

        public Task<RemoteCredentialStoreResult> DeleteAllForProfileAsync(Guid profileId, CancellationToken cancellationToken = default) => Task.FromResult(DeleteResult);
    }
}
