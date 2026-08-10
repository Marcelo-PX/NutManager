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

    private static ManagedNutServerProfiles Document(ManagedNutServerProfile first, ManagedNutServerProfile? second = null, Guid? activeProfileId = null) =>
        new(ManagedNutServerProfiles.CurrentSchemaVersion, activeProfileId ?? first.Id, second is null ? [first] : [first, second]);

    private static string Fingerprint(string value) => SshHostKeyFingerprint.Create(Encoding.UTF8.GetBytes(value));

    private sealed class RecordingStore : IManagedNutServerProfileStore
    {
        public RecordingStore(ManagedNutServerProfiles current) => Current = current;
        public ManagedNutServerProfiles Current { get; set; }
        public int SaveCalls { get; private set; }
        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ManagedNutServerProfiles?>(Current);
        public Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken)
        {
            SaveCalls++;
            Current = profiles;
            return Task.CompletedTask;
        }
    }
}
