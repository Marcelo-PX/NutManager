using NutManager.App.Services;
using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Services;
using Xunit;

namespace NutManager.Tests;

public sealed class ManagedNutServerProfileViewModelTests
{
    [Fact]
    public void NewServerIsOneReversibleDraftWithLocalizedOptions()
    {
        var viewModel = CreateViewModel();

        viewModel.NewServerCommand.Execute(null);

        Assert.True(viewModel.IsCreatingProfile);
        Assert.Equal(string.Empty, viewModel.ProfileDraft.Name);
        Assert.Equal("localhost", viewModel.ProfileDraft.MonitoringHost);
        Assert.Equal(NutManagementMode.Local, viewModel.ProfileDraft.ManagementMode);
        Assert.Equal(ManagedNutServerAccessMode.ReadOnly, viewModel.ProfileDraft.AccessMode);
        Assert.Equal("Somente leitura", viewModel.AccessModeOptions.Single(option => option.Value == ManagedNutServerAccessMode.ReadOnly).Title);
        Assert.Equal("SMB", viewModel.ConfigurationTransportOptions.Single(option => option.Value == RemoteConfigurationTransportKind.Smb).Title);
        Assert.Equal("Identidade atual do Windows", viewModel.SmbAuthenticationOptions.Single(option => option.Value == SmbAuthenticationMode.CurrentWindowsIdentity).Title);
        Assert.DoesNotContain(viewModel.SmbAuthenticationOptions, option => option.Title == nameof(SmbAuthenticationMode.CurrentWindowsIdentity));
    }

    [Fact]
    public void LocalRemoteAndSftpSmbTransitionsPreserveInactiveDraftValues()
    {
        var viewModel = CreateViewModel();
        viewModel.NewServerCommand.Execute(null);
        viewModel.ProfileDraft.ManagementMode = NutManagementMode.Remote;
        viewModel.ProfileDraft.ManagementHost = "ssh.example";
        viewModel.ProfileDraft.SshUsername = "ssh-user";
        viewModel.ProfileDraft.ConfigurationTransport = RemoteConfigurationTransportKind.Smb;
        viewModel.ProfileDraft.SmbSharePath = "\\\\server\\share";
        viewModel.ProfileDraft.SmbUsername = "smb-user";

        viewModel.ProfileDraft.ConfigurationTransport = RemoteConfigurationTransportKind.SshSftp;
        Assert.Equal("ssh.example", viewModel.ProfileDraft.ManagementHost);
        Assert.Equal("ssh-user", viewModel.ProfileDraft.SshUsername);
        viewModel.ProfileDraft.ConfigurationTransport = RemoteConfigurationTransportKind.Smb;
        Assert.Equal("\\\\server\\share", viewModel.ProfileDraft.SmbSharePath);
        Assert.Equal("smb-user", viewModel.ProfileDraft.SmbUsername);
        viewModel.ProfileDraft.ManagementMode = NutManagementMode.Local;
        Assert.False(viewModel.ProfileDraft.IsRemote);
        viewModel.ProfileDraft.ManagementMode = NutManagementMode.Remote;
        Assert.Equal("\\\\server\\share", viewModel.ProfileDraft.SmbSharePath);
    }

    [Fact]
    public void ReadOnlyManageTransitionIsImmediatelyReversible()
    {
        var viewModel = CreateViewModel();
        viewModel.NewServerCommand.Execute(null);

        viewModel.ProfileDraft.AccessMode = ManagedNutServerAccessMode.Manage;
        Assert.Equal(ManagedNutServerAccessMode.Manage, viewModel.ProfileDraft.AccessMode);
        viewModel.ProfileDraft.AccessMode = ManagedNutServerAccessMode.ReadOnly;
        Assert.Equal(ManagedNutServerAccessMode.ReadOnly, viewModel.ProfileDraft.AccessMode);
        Assert.False(viewModel.IsDirtyDraftDecisionVisible);
    }

    [Fact]
    public async Task PersistedModeContainsOnlyApplicableMetadata()
    {
        var initial = Profile("Initial", NutManagementMode.Local);
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, initial.Id, [initial]);
        var store = new RecordingProfileStore(profiles);
        var viewModel = CreateViewModel(profiles, store);
        viewModel.NewServerCommand.Execute(null);
        viewModel.ProfileDraft.Name = "New local";
        viewModel.ProfileDraft.ManagementHost = "hidden.example";
        viewModel.ProfileDraft.SmbSharePath = "\\\\server\\share";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        var local = store.Current!.Profiles.Single(profile => profile.Name == "New local");
        Assert.Equal(NutManagementMode.Local, local.Management.Mode);
        Assert.Null(local.Management.ManagementHost);
        Assert.Null(local.Management.Smb);

        viewModel.NewServerCommand.Execute(null);
        viewModel.ProfileDraft.Name = "New SMB";
        viewModel.ProfileDraft.ManagementMode = NutManagementMode.Remote;
        viewModel.ProfileDraft.ConfigurationTransport = RemoteConfigurationTransportKind.Smb;
        viewModel.ProfileDraft.SmbSharePath = "\\\\server\\share";
        viewModel.ProfileDraft.ManagementHost = "hidden-ssh.example";
        viewModel.ProfileDraft.SshPrivateKeyPath = @"C:\keys\hidden";
        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        var smb = store.Current.Profiles.Single(profile => profile.Name == "New SMB");
        Assert.Equal(RemoteConfigurationTransportKind.Smb, smb.Management.ConfigurationTransport);
        Assert.Null(smb.Management.ManagementHost);
        Assert.Null(smb.Management.SshPrivateKeyPath);

        viewModel.NewServerCommand.Execute(null);
        viewModel.ProfileDraft.Name = "New SFTP";
        viewModel.ProfileDraft.ManagementMode = NutManagementMode.Remote;
        viewModel.ProfileDraft.ManagementHost = "ssh.example";
        viewModel.ProfileDraft.SmbSharePath = "\\\\hidden\\share";
        viewModel.ProfileDraft.SmbUsername = "hidden-user";
        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        var sftp = store.Current.Profiles.Single(profile => profile.Name == "New SFTP");
        Assert.Equal(RemoteConfigurationTransportKind.SshSftp, sftp.Management.ConfigurationTransport);
        Assert.Null(sftp.Management.Smb);
    }

    [Fact]
    public void InlineErrorsDisableSaveWhileWarningsDoNot()
    {
        var viewModel = CreateViewModel();
        viewModel.NewServerCommand.Execute(null);
        viewModel.ProfileDraft.Name = "Server";
        viewModel.ProfileDraft.MonitoringHost = "user@host";

        Assert.False(viewModel.CanSaveProfile);
        Assert.NotEmpty(viewModel.MonitoringHostValidationIssues);

        viewModel.ProfileDraft.MonitoringHost = "monitor.example";
        viewModel.ProfileDraft.ManagementMode = NutManagementMode.Remote;
        viewModel.ProfileDraft.ManagementHost = "management.example";
        viewModel.ProfileDraft.AccessMode = ManagedNutServerAccessMode.Manage;
        viewModel.ProfileDraft.RemoteConfigurationDirectory = null;

        Assert.True(viewModel.CanSaveProfile);
        Assert.Contains(viewModel.RemoteDirectoryValidationIssues, issue => issue.IsWarning);
    }

    [Fact]
    public async Task RenamePreservesIdAndActivationCreatesRestartRequiredStateWithoutChangingRuntime()
    {
        var local = Profile("Local", NutManagementMode.Local);
        var remote = Profile("Remote", NutManagementMode.Remote);
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, local.Id, [local, remote]);
        var store = new RecordingProfileStore(profiles);
        var viewModel = CreateViewModel(profiles, store, runtimeProfileId: local.Id);
        viewModel.ProfileDraft.Name = "Local renamed";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);
        var renamed = store.Current!.Profiles.Single(profile => profile.Name == "Local renamed");
        Assert.Equal(local.Id, renamed.Id);

        viewModel.SelectedManagedProfile = store.Current.Profiles.Single(profile => profile.Id == remote.Id);
        await viewModel.ActivateSelectedProfileCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsActiveProfileRestartRequired);
        Assert.Equal("Local", viewModel.RuntimeProfileName);
        Assert.Equal("Remote", viewModel.PersistedActiveProfileName);
        Assert.Equal(remote.Id, store.Current.ActiveProfileId);

        var afterRestart = CreateViewModel(store.Current, store, runtimeProfileId: remote.Id);
        Assert.False(afterRestart.IsActiveProfileRestartRequired);
    }

    [Fact]
    public async Task ActivatingRuntimeProfileDoesNotRequireRestart()
    {
        var local = Profile("Local", NutManagementMode.Local);
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, local.Id, [local]);
        var viewModel = CreateViewModel(profiles, new RecordingProfileStore(profiles), runtimeProfileId: local.Id);

        Assert.False(viewModel.IsActiveProfileRestartRequired);
        Assert.False(viewModel.CanActivateSelectedProfile);
        await viewModel.ActivateSelectedProfileCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsActiveProfileRestartRequired);
    }

    [Fact]
    public async Task DirtyProfileSwitchOffersContinueDiscardAndSaveDecisions()
    {
        var local = Profile("Local", NutManagementMode.Local);
        var remote = Profile("Remote", NutManagementMode.Remote);
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, local.Id, [local, remote]);
        var store = new RecordingProfileStore(profiles);
        var viewModel = CreateViewModel(profiles, store);
        viewModel.ProfileDraft.Name = "Unsaved";

        viewModel.SelectedManagedProfile = remote;
        Assert.True(viewModel.IsDirtyDraftDecisionVisible);
        Assert.Equal(local.Id, viewModel.SelectedManagedProfile!.Id);
        viewModel.ContinueEditingCommand.Execute(null);
        Assert.False(viewModel.IsDirtyDraftDecisionVisible);
        Assert.Equal("Unsaved", viewModel.ProfileDraft.Name);

        viewModel.SelectedManagedProfile = remote;
        await viewModel.DiscardDirtyDraftAndContinueCommand.ExecuteAsync(null);
        Assert.Equal(remote.Id, viewModel.SelectedManagedProfile!.Id);
        Assert.Equal("Remote", viewModel.ProfileDraft.Name);

        viewModel.ProfileDraft.Name = "Remote saved";
        viewModel.SelectedManagedProfile = local;
        await viewModel.SaveDirtyDraftAndContinueCommand.ExecuteAsync(null);
        Assert.Equal(local.Id, viewModel.SelectedManagedProfile!.Id);
        Assert.Contains(store.Current!.Profiles, profile => profile.Name == "Remote saved" && profile.Id == remote.Id);
    }

    [Fact]
    public async Task FailedDirtySaveKeepsDraftAndPendingDecision()
    {
        var local = Profile("Local", NutManagementMode.Local);
        var remote = Profile("Remote", NutManagementMode.Remote);
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, local.Id, [local, remote]);
        var store = new RecordingProfileStore(profiles) { ThrowOnSave = true };
        var viewModel = CreateViewModel(profiles, store);
        viewModel.ProfileDraft.Name = "Unsaved";
        viewModel.SelectedManagedProfile = remote;

        await viewModel.SaveDirtyDraftAndContinueCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsDirtyDraftDecisionVisible);
        Assert.Equal("Unsaved", viewModel.ProfileDraft.Name);
        Assert.Equal(local.Id, viewModel.SelectedManagedProfile!.Id);
        Assert.NotNull(viewModel.ProfileSaveError);
    }

    [Fact]
    public async Task DirtyNewAndDeleteActionsNeverDiscardSilently()
    {
        var local = Profile("Local", NutManagementMode.Local);
        var remote = Profile("Remote", NutManagementMode.Remote);
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, local.Id, [local, remote]);
        var store = new RecordingProfileStore(profiles);
        var viewModel = CreateViewModel(profiles, store);
        viewModel.ProfileDraft.Name = "Unsaved local";

        viewModel.NewServerCommand.Execute(null);
        Assert.True(viewModel.IsDirtyDraftDecisionVisible);
        Assert.Equal("Unsaved local", viewModel.ProfileDraft.Name);
        viewModel.ContinueEditingCommand.Execute(null);

        viewModel.DiscardProfileDraftCommand.Execute(null);
        viewModel.SelectedManagedProfile = remote;
        viewModel.ProfileDraft.Name = "Unsaved remote";
        await viewModel.DeleteSelectedProfileCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsDirtyDraftDecisionVisible);
        Assert.Contains(store.Current!.Profiles, profile => profile.Id == remote.Id);

        await viewModel.DiscardDirtyDraftAndContinueCommand.ExecuteAsync(null);
        Assert.DoesNotContain(store.Current!.Profiles, profile => profile.Id == remote.Id);
    }

    [Fact]
    public async Task ExplicitConnectionTestDoesNotPersistOrChangeActiveRuntimeProfile()
    {
        var profile = Profile("Runtime", NutManagementMode.Local);
        var profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]);
        var store = new RecordingProfileStore(profiles);
        var tester = new FakeConnectionTester(new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.Success, ["ups-a"]));
        var viewModel = CreateViewModel(profiles, store, tester, profile.Id);
        viewModel.ProfileDraft.MonitoringHost = "test.example";
        viewModel.ProfileDraft.MonitoringPort = "4444";
        viewModel.ProfileDraft.PreferredUpsName = "ups-a";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(ManagedNutConnectionTestStatus.Success, viewModel.ConnectionTestStatus);
        Assert.Equal("test.example", tester.Endpoint!.Host);
        Assert.Equal(4444, tester.Endpoint.Port);
        Assert.Equal("ups-a", tester.PreferredUpsName);
        Assert.Equal(profile.Id, store.Current!.ActiveProfileId);
        Assert.Equal("monitor.example", store.Current.ActiveProfile.Monitoring.Host);
        Assert.Equal(0, store.SaveCalls);
    }

    [Theory]
    [InlineData(ManagedNutConnectionTestStatus.Timeout)]
    [InlineData(ManagedNutConnectionTestStatus.EndpointUnreachable)]
    [InlineData(ManagedNutConnectionTestStatus.ProtocolError)]
    [InlineData(ManagedNutConnectionTestStatus.NoUpsDiscovered)]
    [InlineData(ManagedNutConnectionTestStatus.PreferredUpsMissing)]
    [InlineData(ManagedNutConnectionTestStatus.Cancelled)]
    public async Task ConnectionTestStatusesArePresentedWithoutThrowing(ManagedNutConnectionTestStatus status)
    {
        var tester = new FakeConnectionTester(new ManagedNutConnectionTestResult(status, []));
        var viewModel = CreateViewModel(connectionTester: tester);

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(status, viewModel.ConnectionTestStatus);
        Assert.NotNull(viewModel.ConnectionTestResultText);
    }

    [Fact]
    public async Task ProfileStoreLoadFailureBlocksPersistenceWithoutOverwriting()
    {
        var profile = Profile("Local", NutManagementMode.Local);
        var store = new RecordingProfileStore(new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]));
        var viewModel = CreateViewModel(store.Current, store);
        viewModel.SetProfileLoadError("Malformed", blockPersistence: true);
        viewModel.ProfileDraft.Name = "Changed";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanPersistProfiles);
        Assert.Equal(0, store.SaveCalls);
        Assert.NotNull(viewModel.ProfileSaveError);
    }

    private static SettingsPageViewModel CreateViewModel(
        ManagedNutServerProfiles? profiles = null,
        RecordingProfileStore? profileStore = null,
        IManagedNutConnectionTester? connectionTester = null,
        Guid? runtimeProfileId = null)
    {
        if (profiles is null)
        {
            var profile = Profile("Local", NutManagementMode.Local);
            profiles = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]);
        }

        profileStore ??= new RecordingProfileStore(profiles);
        return new SettingsPageViewModel(
            new ApplicationSettings(),
            new RecordingSettingsStore(),
            profiles,
            profileStore,
            connectionTester: connectionTester,
            runtimeProfileId: runtimeProfileId);
    }

    private static ManagedNutServerProfile Profile(string name, NutManagementMode mode) => new(
        Guid.NewGuid(),
        name,
        new NutMonitoringProfile("monitor.example"),
        mode == NutManagementMode.Local
            ? new NutManagementProfile(NutManagementMode.Local)
            : new NutManagementProfile(NutManagementMode.Remote, "management.example", "/etc/nut"),
        ManagedNutServerAccessMode.Manage);

    private sealed class RecordingProfileStore : IManagedNutServerProfileStore
    {
        public RecordingProfileStore(ManagedNutServerProfiles? current = null) => Current = current;

        public ManagedNutServerProfiles? Current { get; private set; }

        public bool ThrowOnSave { get; set; }

        public int SaveCalls { get; private set; }

        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (ThrowOnSave)
            {
                throw new IOException("Simulated failure");
            }

            Current = profiles;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSettingsStore : IApplicationSettingsStore
    {
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new ApplicationSettings());

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeConnectionTester : IManagedNutConnectionTester
    {
        private readonly ManagedNutConnectionTestResult _result;

        public FakeConnectionTester(ManagedNutConnectionTestResult result) => _result = result;

        public NutEndpoint? Endpoint { get; private set; }

        public string? PreferredUpsName { get; private set; }

        public Task<ManagedNutConnectionTestResult> TestAsync(NutEndpoint endpoint, string? preferredUpsName, CancellationToken cancellationToken)
        {
            Endpoint = endpoint;
            PreferredUpsName = preferredUpsName;
            return Task.FromResult(_result);
        }
    }
}
