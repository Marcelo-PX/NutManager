using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Services;
using Xunit;

namespace NutManager.Tests;

public sealed class ManagedNutServerProfileViewModelTests
{
    [Fact]
    public void CreateProfilesStartReadOnlyAndRemoteRequiresUserInput()
    {
        var viewModel = CreateViewModel();

        viewModel.CreateLocalProfileCommand.Execute(null);
        Assert.True(viewModel.IsCreatingProfile);
        Assert.Equal("Novo servidor local", viewModel.ProfileDraft.Name);
        Assert.Equal("localhost", viewModel.ProfileDraft.MonitoringHost);
        Assert.Equal(ManagedNutServerAccessMode.ReadOnly, viewModel.ProfileDraft.AccessMode);

        viewModel.DiscardProfileDraftCommand.Execute(null);
        viewModel.CreateRemoteProfileCommand.Execute(null);
        Assert.Equal("Novo servidor remoto", viewModel.ProfileDraft.Name);
        Assert.True(viewModel.ProfileDraft.IsRemote);
        Assert.Equal(string.Empty, viewModel.ProfileDraft.MonitoringHost);
        Assert.Equal(string.Empty, viewModel.ProfileDraft.ManagementHost);
        Assert.Equal(ManagedNutServerAccessMode.ReadOnly, viewModel.ProfileDraft.AccessMode);
    }

    [Fact]
    public async Task SaveRenameActivateAndDeleteUseTheProfileStoreWithoutChangingRuntime()
    {
        var local = Profile("Local", NutManagementMode.Local);
        var remote = Profile("Remote", NutManagementMode.Remote, "management.example", "/etc/nut");
        var store = new RecordingProfileStore();
        var viewModel = CreateViewModel(new ManagedNutServerProfiles(1, local.Id, [local, remote]), store);

        viewModel.ProfileDraft.Name = "Local renamed";
        await viewModel.SaveProfileCommand.ExecuteAsync(null);
        var renamed = store.Saved!.Profiles.Single(profile => profile.Name == "Local renamed");
        Assert.Equal(local.Id, renamed.Id);

        viewModel.SelectedManagedProfile = viewModel.ManagedProfiles.Single(profile => profile.Id == remote.Id);
        await viewModel.ActivateSelectedProfileCommand.ExecuteAsync(null);
        Assert.Equal(remote.Id, store.Saved!.ActiveProfileId);
        Assert.Contains("Reinicie", viewModel.ProfileStatusMessage);

        viewModel.SelectedManagedProfile = viewModel.ManagedProfiles.Single(profile => profile.Id == local.Id);
        await viewModel.DeleteSelectedProfileCommand.ExecuteAsync(null);
        Assert.Single(store.Saved!.Profiles);
        Assert.Equal(remote.Id, store.Saved.ActiveProfileId);
    }

    [Fact]
    public void DirtyDraftBlocksProfileSelectionAndActiveOrLastProfileDeletion()
    {
        var local = Profile("Local", NutManagementMode.Local);
        var remote = Profile("Remote", NutManagementMode.Remote, "management.example", "/etc/nut");
        var viewModel = CreateViewModel(new ManagedNutServerProfiles(1, local.Id, [local, remote]));

        viewModel.ProfileDraft.Name = "Unsaved";
        viewModel.CreateRemoteProfileCommand.Execute(null);
        Assert.Equal("Unsaved", viewModel.ProfileDraft.Name);
        viewModel.SelectedManagedProfile = remote;
        Assert.Equal(local.Id, viewModel.SelectedManagedProfile!.Id);
        Assert.Contains("Salve ou descarte", viewModel.ProfileSaveError);

        viewModel.DiscardProfileDraftCommand.Execute(null);
        Assert.False(viewModel.IsProfileDraftDirty);
        Assert.False(viewModel.CanDeleteSelectedProfile);
        viewModel.SelectedManagedProfile = remote;
        Assert.True(viewModel.CanDeleteSelectedProfile);
    }

    [Fact]
    public async Task GeneralSettingsMirrorTheActiveProfileEndpointWhenProfilesArePresent()
    {
        var local = new ManagedNutServerProfile(
            Guid.NewGuid(),
            "Local",
            new NutMonitoringProfile("profile-host", 4444, "profile-ups"),
            new NutManagementProfile(NutManagementMode.Local),
            ManagedNutServerAccessMode.Manage);
        var settingsStore = new RecordingSettingsStore();
        var viewModel = new SettingsPageViewModel(
            new ApplicationSettings(host: "legacy-host", port: 3493),
            settingsStore,
            new ManagedNutServerProfiles(1, local.Id, [local]),
            new RecordingProfileStore());
        viewModel.Host = "ignored-host";
        viewModel.Port = "9999";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("profile-host", settingsStore.Saved!.Host);
        Assert.Equal(4444, settingsStore.Saved.Port);
        Assert.Equal("profile-ups", settingsStore.Saved.PreferredUpsName);
    }

    [Fact]
    public async Task ProfileStoreFailureIsVisibleAndDoesNotChangeConfirmedProfiles()
    {
        var profile = Profile("Local", NutManagementMode.Local);
        var viewModel = CreateViewModel(new ManagedNutServerProfiles(1, profile.Id, [profile]), new FailingProfileStore());
        viewModel.ProfileDraft.Name = "Changed";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ProfileSaveError);
        Assert.Equal("Local", Assert.Single(viewModel.ManagedProfiles).Name);
    }

    [Fact]
    public async Task ExistingProfileLoadFailureBlocksPersistenceInsteadOfOverwritingTheFile()
    {
        var profile = Profile("Local", NutManagementMode.Local);
        var store = new RecordingProfileStore();
        var viewModel = CreateViewModel(new ManagedNutServerProfiles(1, profile.Id, [profile]), store);
        viewModel.SetProfileLoadError("Arquivo malformado", blockPersistence: true);
        viewModel.ProfileDraft.Name = "Changed";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanPersistProfiles);
        Assert.Null(store.Saved);
        Assert.NotNull(viewModel.ProfileSaveError);
    }

    private static SettingsPageViewModel CreateViewModel(ManagedNutServerProfiles? profiles = null, IManagedNutServerProfileStore? profileStore = null)
    {
        if (profiles is null)
        {
            var profile = Profile("Local", NutManagementMode.Local);
            profiles = new ManagedNutServerProfiles(1, profile.Id, [profile]);
        }

        return new SettingsPageViewModel(new ApplicationSettings(), new RecordingSettingsStore(), profiles, profileStore ?? new RecordingProfileStore());
    }

    private static ManagedNutServerProfile Profile(string name, NutManagementMode mode, string? managementHost = null, string? remoteDirectory = null) => new(
        Guid.NewGuid(),
        name,
        new NutMonitoringProfile("monitor.example"),
        new NutManagementProfile(mode, managementHost, remoteDirectory),
        ManagedNutServerAccessMode.Manage);

    private sealed class RecordingProfileStore : IManagedNutServerProfileStore
    {
        public ManagedNutServerProfiles? Saved { get; private set; }

        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ManagedNutServerProfiles?>(null);

        public Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken)
        {
            Saved = profiles;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingProfileStore : IManagedNutServerProfileStore
    {
        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ManagedNutServerProfiles?>(null);

        public Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken) => throw new IOException();
    }

    private sealed class RecordingSettingsStore : IApplicationSettingsStore
    {
        public ApplicationSettings? Saved { get; private set; }

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new ApplicationSettings());

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }
}
