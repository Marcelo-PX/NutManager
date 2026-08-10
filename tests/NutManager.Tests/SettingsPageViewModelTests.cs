using NutManager.App.ViewModels;
using NutManager.App.Services;
using NutManager.Core.Models;
using NutManager.Core.Services;
using Xunit;

namespace NutManager.Tests;

public sealed class SettingsPageViewModelTests
{
    [Fact]
    public async Task ThemePersistenceUsesOnlyTheConfirmedSettings()
    {
        var saved = new List<ApplicationSettings>();
        var confirmed = new ApplicationSettings(host: "saved", port: 3493, mockMode: true);
        var viewModel = new SettingsPageViewModel(confirmed, new RecordingStore(saved));
        viewModel.Host = "pending-host";
        viewModel.Port = "9999";
        viewModel.MockMode = false;

        await viewModel.PersistThemeAsync(ThemePreference.Dark);

        var persisted = Assert.Single(saved);
        Assert.Equal("saved", persisted.Host);
        Assert.Equal(3493, persisted.Port);
        Assert.True(persisted.MockMode);
        Assert.Equal(ThemePreference.Dark, persisted.Theme);
    }

    [Fact]
    public async Task ExplicitSaveBecomesTheBaseForFutureThemePersistence()
    {
        var saved = new List<ApplicationSettings>();
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new RecordingStore(saved));
        viewModel.Host = "confirmed-after-save";
        await viewModel.SaveCommand.ExecuteAsync(null);
        viewModel.Host = "pending-after-save";

        await viewModel.PersistThemeAsync(ThemePreference.Light);

        Assert.Equal("confirmed-after-save", saved.Last().Host);
        Assert.Equal(ThemePreference.Light, saved.Last().Theme);
    }

    [Fact]
    public async Task LoadErrorPreventsAutomaticThemePersistenceButExplicitSaveRemainsAvailable()
    {
        var saved = new List<ApplicationSettings>();
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new RecordingStore(saved));
        viewModel.SetLoadError("settings inválido");

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
    public async Task ConstructorAndSaveHandleAllSettingsValues()
    {
        var saved = new List<ApplicationSettings>();
        var source = new ApplicationSettings(host: "host", port: 4321, preferredUpsName: "ups", pollingInterval: TimeSpan.FromSeconds(8), connectionTimeout: TimeSpan.FromSeconds(4), theme: ThemePreference.Light, mockMode: false);
        var viewModel = new SettingsPageViewModel(source, new RecordingStore(saved));
        Assert.Equal("host", viewModel.Host); Assert.Equal("4321", viewModel.Port); Assert.Equal("ups", viewModel.PreferredUpsName);
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal(source, Assert.Single(saved)); Assert.True(viewModel.IsSaved); Assert.False(viewModel.IsSaving);
    }

    [Theory]
    [InlineData("bad", "5", "5", "localhost")]
    [InlineData("0", "5", "5", "localhost")]
    [InlineData("3493", "0", "5", "localhost")]
    [InlineData("3493", "5", "0", "localhost")]
    [InlineData("3493", "5", "5", " ")]
    public async Task InvalidFormValuesSetSaveError(string port, string polling, string timeout, string host)
    {
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new RecordingStore([])) { Port = port, PollingIntervalSeconds = polling, ConnectionTimeoutSeconds = timeout, Host = host };
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.SaveError); Assert.False(viewModel.IsSaving); Assert.False(viewModel.IsSaved);
    }

    [Fact]
    public async Task EmptyPreferredUpsIsSavedAndStoreFailureSetsError()
    {
        var saved = new List<ApplicationSettings>();
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new RecordingStore(saved)) { PreferredUpsName = "" };
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Null(Assert.Single(saved).PreferredUpsName);
        var failure = new SettingsPageViewModel(new ApplicationSettings(), new FailingStore());
        await failure.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(failure.SaveError); Assert.False(failure.IsSaving); Assert.False(failure.IsSaved);
    }

    [Fact]
    public async Task ProfileSaveAfterCredentialRemovalClearlyReportsThePartialOutcome()
    {
        var profile = RemoteProfile();
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]);
        var profileStore = new ProfileStore(profiles) { ThrowOnSave = true };
        var credentials = new CredentialStore();
        var viewModel = new SettingsPageViewModel(
            new ApplicationSettings(),
            null,
            profiles,
            profileStore,
            new ManagedNutServerProfileUpdateService(profileStore, credentials),
            credentials);
        viewModel.ProfileDraft.ManagementHost = "new-management.example";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsProfileSaved);
        Assert.Equal("management.example", profileStore.Current.ActiveProfile.Management.ManagementHost);
        Assert.Contains("não pôde ser salvo", viewModel.ProfileSaveError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("credenciais protegidas", viewModel.ProfileSaveError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("novamente", viewModel.ProfileSaveError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProfileSaveBlockedByCredentialRemovalFailureExplainsThatTheProfileWasNotChanged()
    {
        var profile = RemoteProfile();
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]);
        var profileStore = new ProfileStore(profiles);
        var credentials = new CredentialStore { DeleteResult = new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.AccessDenied) };
        var viewModel = new SettingsPageViewModel(
            new ApplicationSettings(),
            null,
            profiles,
            profileStore,
            new ManagedNutServerProfileUpdateService(profileStore, credentials),
            credentials);
        viewModel.ProfileDraft.ManagementHost = "new-management.example";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsProfileSaved);
        Assert.Equal("management.example", profileStore.Current.ActiveProfile.Management.ManagementHost);
        Assert.Contains("não foi alterado", viewModel.ProfileSaveError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não pôde ser removida", viewModel.ProfileSaveError, StringComparison.OrdinalIgnoreCase);
    }

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
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) { _settings.Add(settings); return Task.CompletedTask; }
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
