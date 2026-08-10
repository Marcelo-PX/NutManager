using System.Text;
using NutManager.App.Services;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Remote.Ssh;
using Xunit;

namespace NutManager.Tests;

public sealed class ManagedNutServerProfileUpdateServiceTests
{
    [Fact]
    public async Task TrustHostKeyPreservesCurrentUnrelatedMetadata()
    {
        var expected = Profile(directory: "/old/nut");
        var current = Copy(expected, directory: "/current/nut", name: "Current", monitoringHost: "current-monitor", accessMode: ManagedNutServerAccessMode.ReadOnly);
        var other = Profile(name: "Other");
        var store = new RecordingStore(Document(current, other, current.Id));

        var updated = await new ManagedNutServerProfileUpdateService(store).TrustHostKeyAsync(expected, "ssh-ed25519", Fingerprint("new-key"));

        Assert.NotNull(updated);
        Assert.Equal("/current/nut", updated!.Management.RemoteConfigurationDirectory);
        Assert.Equal("Current", updated.Name);
        Assert.Equal("current-monitor", updated.Monitoring.Host);
        Assert.Equal(ManagedNutServerAccessMode.ReadOnly, updated.AccessMode);
        Assert.Equal(Fingerprint("new-key"), updated.Management.TrustedHostKeyFingerprint);
        Assert.Equal(current.Id, store.Current.ActiveProfileId);
        Assert.Equal(other, store.Current.Profiles.Single(profile => profile.Id == other.Id));
    }

    [Fact]
    public async Task TrustHostKeyRejectsConcurrentTrustMetadataChange()
    {
        var expected = Profile();
        var store = new RecordingStore(Document(Copy(expected, fingerprint: Fingerprint("already-trusted"))));

        var updated = await new ManagedNutServerProfileUpdateService(store).TrustHostKeyAsync(expected, "ssh-ed25519", Fingerprint("new-key"));

        Assert.Null(updated);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task SaveDirectoryPreservesCurrentFingerprintButRejectsStaleSessionIdentity()
    {
        var fingerprint = Fingerprint("trusted");
        var expected = Profile(directory: "/old/nut", fingerprint: fingerprint);
        var current = Copy(expected, directory: "/current/nut", name: "Current");
        var store = new RecordingStore(Document(current));
        var service = new ManagedNutServerProfileUpdateService(store);

        var updated = await service.SaveRemoteDirectoryAsync(expected, "/new/nut");

        Assert.NotNull(updated);
        Assert.Equal("/new/nut", updated!.Management.RemoteConfigurationDirectory);
        Assert.Equal(fingerprint, updated.Management.TrustedHostKeyFingerprint);

        var stale = Copy(updated, fingerprint: fingerprint);
        store.Current = Document(Copy(updated, fingerprint: null, replaceFingerprint: true));
        var rejected = await service.SaveRemoteDirectoryAsync(stale, "/must-not-overwrite");

        Assert.Null(rejected);
        Assert.Null(store.Current.ActiveProfile.Management.TrustedHostKeyFingerprint);
        Assert.Equal("/new/nut", store.Current.ActiveProfile.Management.RemoteConfigurationDirectory);
    }

    [Fact]
    public async Task ProfileUpdatesLeaveMonitoringAccessAndActiveProfileUntouched()
    {
        var profile = Profile(fingerprint: Fingerprint("trusted"));
        var active = Profile(name: "Active");
        var store = new RecordingStore(Document(profile, active, active.Id));

        var updated = await new ManagedNutServerProfileUpdateService(store).SaveRemoteDirectoryAsync(profile, "/new/nut");

        Assert.NotNull(updated);
        Assert.Equal(active.Id, store.Current.ActiveProfileId);
        Assert.Equal(profile.Monitoring, updated!.Monitoring);
        Assert.Equal(profile.AccessMode, updated.AccessMode);
        Assert.Equal(active, store.Current.ActiveProfile);
    }

    [Fact]
    public async Task ForgetTrustedHostKeyCannotBeRevertedByAnOldDirectorySession()
    {
        var profile = Profile(fingerprint: Fingerprint("trusted"));
        var store = new RecordingStore(Document(profile));
        var service = new ManagedNutServerProfileUpdateService(store);

        var forgotten = await service.ForgetTrustedHostKeyAsync(profile);
        var staleDirectorySave = await service.SaveRemoteDirectoryAsync(profile, "/stale/nut");

        Assert.NotNull(forgotten);
        Assert.Null(staleDirectorySave);
        Assert.Null(store.Current.ActiveProfile.Management.TrustedHostKeyFingerprint);
        Assert.Equal("/etc/nut", store.Current.ActiveProfile.Management.RemoteConfigurationDirectory);
    }

    [Fact]
    public async Task CreateUsesCurrentProfilesAndPreservesActiveProfile()
    {
        var active = Profile(name: "Active");
        var existing = Profile(name: "Existing");
        var created = Profile(name: "Created");
        var store = new RecordingStore(Document(active, existing, active.Id));

        var document = await new ManagedNutServerProfileUpdateService(store).CreateProfileAsync(created);

        Assert.NotNull(document);
        Assert.Equal(active.Id, document!.ActiveProfileId);
        Assert.Equal(existing, document.Profiles.Single(profile => profile.Id == existing.Id));
        Assert.Equal(created, document.Profiles.Single(profile => profile.Id == created.Id));
    }

    [Fact]
    public async Task DeleteRevalidatesCurrentActiveAndLastProfileRules()
    {
        var active = Profile(name: "Active");
        var removable = Profile(name: "Removable");
        var store = new RecordingStore(Document(active, removable, active.Id));
        var service = new ManagedNutServerProfileUpdateService(store);

        store.Current = Document(active);
        var rejectedLast = await service.DeleteProfileAsync(removable.Id);
        store.Current = Document(active, removable, removable.Id);
        var rejectedActive = await service.DeleteProfileAsync(removable.Id);

        Assert.Null(rejectedLast);
        Assert.Null(rejectedActive);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task ActivateUsesTheCurrentProfileList()
    {
        var first = Profile(name: "First");
        var second = Profile(name: "Second");
        var store = new RecordingStore(Document(first, second, first.Id));

        var document = await new ManagedNutServerProfileUpdateService(store).ActivateProfileAsync(second.Id);

        Assert.NotNull(document);
        Assert.Equal(second.Id, document!.ActiveProfileId);
        Assert.Equal(first, document.Profiles.Single(profile => profile.Id == first.Id));
    }

    [Fact]
    public async Task SettingsProfileSavePreservesCurrentTrustMetadata()
    {
        var trusted = Profile(fingerprint: Fingerprint("trusted"));
        var staleDraft = Copy(trusted, fingerprint: Fingerprint("other"), name: "Renamed");
        var store = new RecordingStore(Document(trusted));

        var document = await new ManagedNutServerProfileUpdateService(store).SaveExistingProfileAsync(trusted, staleDraft);

        var saved = Assert.Single(Assert.IsType<ManagedNutServerProfiles>(document).Profiles);
        Assert.Equal("Renamed", saved.Name);
        Assert.Equal(trusted.Management.TrustedHostKeyFingerprint, saved.Management.TrustedHostKeyFingerprint);
        Assert.Equal(trusted.Management.TrustedHostKeyAlgorithm, saved.Management.TrustedHostKeyAlgorithm);
    }

    [Fact]
    public async Task ChangingSshIdentityDeletesOnlyTheTwoKnownSshCredentialsBeforeSavingMetadata()
    {
        var current = Profile();
        var updated = CreateSshProfile(current.Id, host: "new-management.example");
        var store = new RecordingStore(Document(current));
        var credentials = new RecordingCredentialStore();

        var saved = await new ManagedNutServerProfileUpdateService(store, credentials).SaveExistingProfileAsync(current, updated);

        Assert.NotNull(saved);
        Assert.Equal(
            [RemoteCredentialKind.SshPassword, RemoteCredentialKind.SshPrivateKeyPassphrase],
            credentials.DeletedKinds);
        Assert.Equal("new-management.example", store.Current.ActiveProfile.Management.ManagementHost);
    }

    [Fact]
    public async Task CredentialCleanupFailureAbortsAnSshIdentityChangeBeforeMetadataSave()
    {
        var current = Profile();
        var updated = CreateSshProfile(current.Id, host: "new-management.example");
        var store = new RecordingStore(Document(current));
        var credentials = new RecordingCredentialStore { DeleteResult = new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.AccessDenied) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ManagedNutServerProfileUpdateService(store, credentials).SaveExistingProfileAsync(current, updated));

        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(current, store.Current.ActiveProfile);
    }

    [Fact]
    public async Task SavingAfterCredentialCleanupFailureLeavesOldMetadataAndRemovedCredentialStateAccurate()
    {
        var current = Profile();
        var updated = CreateSshProfile(current.Id, host: "new-management.example");
        var store = new RecordingStore(Document(current)) { ThrowOnSave = true };
        var credentials = new RecordingCredentialStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ManagedNutServerProfileUpdateService(store, credentials).SaveExistingProfileAsync(current, updated));

        Assert.Equal(current, store.Current.ActiveProfile);
        Assert.Equal(2, credentials.DeletedKinds.Count);
    }

    [Fact]
    public async Task ProfileRenameDoesNotInvalidateCredentials()
    {
        var current = Profile();
        var renamed = new ManagedNutServerProfile(current.Id, "Renamed", current.Monitoring, current.Management, current.AccessMode);
        var store = new RecordingStore(Document(current));
        var credentials = new RecordingCredentialStore();

        var saved = await new ManagedNutServerProfileUpdateService(store, credentials).SaveExistingProfileAsync(current, renamed);

        Assert.NotNull(saved);
        Assert.Empty(credentials.DeletedKinds);
    }

    [Fact]
    public async Task DeletingAnInactiveProfileRemovesOnlyItsKnownCredentialTargetsFirst()
    {
        var active = Profile(name: "Active");
        var removable = Profile(name: "Removable");
        var store = new RecordingStore(Document(active, removable, active.Id));
        var credentials = new RecordingCredentialStore();

        var saved = await new ManagedNutServerProfileUpdateService(store, credentials).DeleteProfileAsync(removable.Id);

        Assert.NotNull(saved);
        Assert.Equal([removable.Id], credentials.DeleteAllProfileIds);
        Assert.DoesNotContain(store.Current.Profiles, profile => profile.Id == removable.Id);
    }

    [Fact]
    public async Task CredentialCleanupFailureAbortsProfileDeletion()
    {
        var active = Profile(name: "Active");
        var removable = Profile(name: "Removable");
        var store = new RecordingStore(Document(active, removable, active.Id));
        var credentials = new RecordingCredentialStore { DeleteAllResult = new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.AccessDenied) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ManagedNutServerProfileUpdateService(store, credentials).DeleteProfileAsync(removable.Id));

        Assert.Equal(0, store.SaveCalls);
        Assert.Contains(store.Current.Profiles, profile => profile.Id == removable.Id);
    }

    [Fact]
    public async Task StaleRemoteSessionCannotPersistCredentialAfterSshIdentityChanges()
    {
        var expected = Profile();
        var current = CreateSshProfile(expected.Id, host: "new-management.example");
        var store = new RecordingStore(Document(current));
        var credentials = new RecordingCredentialStore();
        var service = new ManagedNutServerProfileUpdateService(store, credentials);

        var result = await service.SaveCredentialForCurrentSessionAsync(expected, RemoteCredentialKind.SshPassword, "fictional-password".AsMemory());

        Assert.Equal(RemoteCredentialStoreStatus.Failed, result.Status);
        Assert.Equal(0, credentials.WriteCalls);
    }

    [Fact]
    public async Task StaleRemoteSessionCannotPersistCredentialAfterSmbIdentityChanges()
    {
        var expected = CreateSmbProfile(Guid.NewGuid(), @"\\server\share", "DOMAIN\\nut");
        var current = CreateSmbProfile(expected.Id, @"\\server\other-share", "DOMAIN\\nut");
        var credentials = new RecordingCredentialStore();
        var service = new ManagedNutServerProfileUpdateService(new RecordingStore(Document(current)), credentials);

        var result = await service.SaveCredentialForCurrentSessionAsync(expected, RemoteCredentialKind.SmbPassword, "fictional-password".AsMemory());

        Assert.Equal(RemoteCredentialStoreStatus.Failed, result.Status);
        Assert.Equal(0, credentials.WriteCalls);
    }

    [Fact]
    public async Task MatchingRemoteSessionCanPersistItsAllowedCredential()
    {
        var profile = Profile();
        var credentials = new RecordingCredentialStore();
        var service = new ManagedNutServerProfileUpdateService(new RecordingStore(Document(profile)), credentials);

        var result = await service.SaveCredentialForCurrentSessionAsync(profile, RemoteCredentialKind.SshPassword, "fictional-password".AsMemory());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, credentials.WriteCalls);
        Assert.Equal(RemoteCredentialKind.SshPassword, credentials.LastWrittenKind);
    }

    [Fact]
    public async Task PrivateKeyMetadataSurvivesTrustAndDirectoryMutations()
    {
        var profile = CreateSshProfile(Guid.NewGuid(), authenticationMode: SshAuthenticationMode.PrivateKey, keyPath: @"C:\keys\fictional.key");
        var store = new RecordingStore(Document(profile));
        var service = new ManagedNutServerProfileUpdateService(store);

        var trusted = await service.TrustHostKeyAsync(profile, "ssh-ed25519", Fingerprint("trusted"));
        var directory = await service.SaveRemoteDirectoryAsync(trusted!, "/new/nut");

        Assert.Equal(SshAuthenticationMode.PrivateKey, directory!.Management.SshAuthenticationMode);
        Assert.Equal(@"C:\keys\fictional.key", directory.Management.SshPrivateKeyPath);
    }

    private static ManagedNutServerProfile Profile(
        string name = "Remote",
        string directory = "/etc/nut",
        string? fingerprint = null) => new(
        Guid.NewGuid(),
        name,
        new NutMonitoringProfile("monitor.example", 3493, "ups-a"),
        new NutManagementProfile(NutManagementMode.Remote, "management.example", directory, 22, "nutadmin", fingerprint, fingerprint is null ? null : "ssh-ed25519"),
        ManagedNutServerAccessMode.Manage);

    private static ManagedNutServerProfile Copy(
        ManagedNutServerProfile profile,
        string? directory = null,
        string? fingerprint = null,
        bool replaceFingerprint = false,
        string? name = null,
        string? monitoringHost = null,
        ManagedNutServerAccessMode? accessMode = null) => new(
        profile.Id,
        name ?? profile.Name,
        new NutMonitoringProfile(monitoringHost ?? profile.Monitoring.Host, profile.Monitoring.Port, profile.Monitoring.PreferredUpsName),
        new NutManagementProfile(
            NutManagementMode.Remote,
            profile.Management.ManagementHost,
            directory ?? profile.Management.RemoteConfigurationDirectory,
            profile.Management.SshPort,
            profile.Management.SshUsername,
            replaceFingerprint ? fingerprint : fingerprint ?? profile.Management.TrustedHostKeyFingerprint,
            replaceFingerprint
                ? fingerprint is null ? null : "ssh-ed25519"
                : profile.Management.TrustedHostKeyAlgorithm),
        accessMode ?? profile.AccessMode);

    private static ManagedNutServerProfile CreateSshProfile(
        Guid id,
        string host = "management.example",
        SshAuthenticationMode authenticationMode = SshAuthenticationMode.Password,
        string? keyPath = null) => new(
        id,
        "Remote",
        new NutMonitoringProfile("monitor.example", 3493, "ups-a"),
        new NutManagementProfile(
            NutManagementMode.Remote,
            host,
            "/etc/nut",
            22,
            "nutadmin",
            sshAuthenticationMode: authenticationMode,
            sshPrivateKeyPath: keyPath),
        ManagedNutServerAccessMode.Manage);

    private static ManagedNutServerProfile CreateSmbProfile(Guid id, string sharePath, string username) => new(
        id,
        "SMB",
        new NutMonitoringProfile("monitor.example", 3493, "ups-a"),
        new NutManagementProfile(
            NutManagementMode.Remote,
            configurationTransport: RemoteConfigurationTransportKind.Smb,
            smbSharePath: sharePath,
            smbAuthenticationMode: SmbAuthenticationMode.ExplicitCredentials,
            smbUsername: username),
        ManagedNutServerAccessMode.Manage);

    private static ManagedNutServerProfiles Document(ManagedNutServerProfile first, ManagedNutServerProfile? second = null, Guid? activeProfileId = null) =>
        new(ManagedNutServerProfiles.CurrentSchemaVersion, activeProfileId ?? first.Id, second is null ? [first] : [first, second]);

    private static string Fingerprint(string value) => SshHostKeyFingerprint.Create(Encoding.UTF8.GetBytes(value));

    private sealed class RecordingStore : IManagedNutServerProfileStore
    {
        public RecordingStore(ManagedNutServerProfiles current) => Current = current;
        public ManagedNutServerProfiles Current { get; set; }
        public int SaveCalls { get; private set; }
        public bool ThrowOnSave { get; set; }
        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ManagedNutServerProfiles?>(Current);
        public Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("Simulated persistence failure.");
            }

            Current = profiles;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCredentialStore : IRemoteCredentialStore
    {
        public List<RemoteCredentialKind> DeletedKinds { get; } = [];
        public List<Guid> DeleteAllProfileIds { get; } = [];
        public RemoteCredentialStoreResult DeleteResult { get; set; } = new(RemoteCredentialStoreStatus.Success);
        public RemoteCredentialStoreResult DeleteAllResult { get; set; } = new(RemoteCredentialStoreStatus.Success);
        public int WriteCalls { get; private set; }
        public RemoteCredentialKind? LastWrittenKind { get; private set; }

        public Task<RemoteCredentialStoreResult> ContainsAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.NotFound));

        public Task<RemoteCredentialReadResult> ReadAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteCredentialReadResult(RemoteCredentialStoreStatus.NotFound));

        public Task<RemoteCredentialStoreResult> WriteAsync(Guid profileId, RemoteCredentialKind kind, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            LastWrittenKind = kind;
            return Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success));
        }

        public Task<RemoteCredentialStoreResult> DeleteAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default)
        {
            DeletedKinds.Add(kind);
            return Task.FromResult(DeleteResult);
        }

        public Task<RemoteCredentialStoreResult> DeleteAllForProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
        {
            DeleteAllProfileIds.Add(profileId);
            return Task.FromResult(DeleteAllResult);
        }
    }
}
