using NutManager.App.Services;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Persistence;
using Xunit;

namespace NutManager.Tests;

public sealed class ManagedNutServerProfileTests
{
    [Fact]
    public void ValidLocalAndRemoteProfilesKeepMonitoringAndManagementIndependent()
    {
        var local = Profile("Local", NutManagementMode.Local);
        var remote = Profile("Remote", NutManagementMode.Remote, "management.example", "/etc/nut");

        Assert.Equal("monitor.example", local.Monitoring.Host);
        Assert.Null(local.Management.ManagementHost);
        Assert.Equal("management.example", remote.Management.ManagementHost);
        Assert.Equal("/etc/nut", remote.Management.RemoteConfigurationDirectory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void InvalidMonitoringPortsAreRejected(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new NutMonitoringProfile("server", port));

    [Fact]
    public void ProfileValidationRejectsEmptyIdentifiersNamesHostsAndInvalidEnums()
    {
        Assert.Throws<ArgumentException>(() => new ManagedNutServerProfile(Guid.Empty, "name", new NutMonitoringProfile("server"), new NutManagementProfile(NutManagementMode.Local), ManagedNutServerAccessMode.Manage));
        Assert.Throws<ArgumentException>(() => new ManagedNutServerProfile(Guid.NewGuid(), " ", new NutMonitoringProfile("server"), new NutManagementProfile(NutManagementMode.Local), ManagedNutServerAccessMode.Manage));
        Assert.Throws<ArgumentException>(() => new NutMonitoringProfile("bad\nserver"));
        Assert.Throws<ArgumentException>(() => new NutManagementProfile(NutManagementMode.Remote, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NutManagementProfile((NutManagementMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedNutServerProfile(Guid.NewGuid(), "name", new NutMonitoringProfile("server"), new NutManagementProfile(NutManagementMode.Local), (ManagedNutServerAccessMode)99));
    }

    [Fact]
    public void PreferredUpsAndRemotePathsAreNormalizedWithoutLocalPathInterpretation()
    {
        var profile = new ManagedNutServerProfile(
            Guid.NewGuid(),
            "Remote",
            new NutMonitoringProfile("server", preferredUpsName: "  "),
            new NutManagementProfile(NutManagementMode.Remote, "mgmt", "  C:\\NUT\\etc  "),
            ManagedNutServerAccessMode.Manage);

        Assert.Null(profile.Monitoring.PreferredUpsName);
        Assert.Equal("C:\\NUT\\etc", profile.Management.RemoteConfigurationDirectory);
        var posix = Profile("Remote2", NutManagementMode.Remote, "mgmt", "/opt/nut/etc");
        Assert.Equal("/opt/nut/etc", posix.Management.RemoteConfigurationDirectory);
    }

    [Fact]
    public void ProfileDocumentRequiresUniqueIdsUniqueNamesAndAnExistingActiveProfile()
    {
        var first = Profile("First", NutManagementMode.Local);
        var sameName = Profile("first", NutManagementMode.Local);
        var sameId = new ManagedNutServerProfile(first.Id, "Other", new NutMonitoringProfile("monitor.example"), new NutManagementProfile(NutManagementMode.Local), ManagedNutServerAccessMode.Manage);

        Assert.Throws<ArgumentException>(() => new ManagedNutServerProfiles(1, first.Id, [first, sameName]));
        Assert.Throws<ArgumentException>(() => new ManagedNutServerProfiles(1, first.Id, [first, sameId]));
        Assert.Throws<ArgumentException>(() => new ManagedNutServerProfiles(1, Guid.NewGuid(), [first]));
        Assert.Throws<ArgumentException>(() => new ManagedNutServerProfiles(1, Guid.Empty, Array.Empty<ManagedNutServerProfile>()));
    }

    [Fact]
    public void RenameKeepsProfileIdentity()
    {
        var profile = Profile("Before", NutManagementMode.Local);
        var renamed = new ManagedNutServerProfile(profile.Id, "After", profile.Monitoring, profile.Management, profile.AccessMode);
        Assert.Equal(profile.Id, renamed.Id);
    }

    [Theory]
    [InlineData(NutManagementMode.Local, ManagedNutServerAccessMode.Manage, true, true, true, true)]
    [InlineData(NutManagementMode.Local, ManagedNutServerAccessMode.ReadOnly, true, false, false, false)]
    [InlineData(NutManagementMode.Remote, ManagedNutServerAccessMode.Manage, false, false, false, false)]
    [InlineData(NutManagementMode.Remote, ManagedNutServerAccessMode.ReadOnly, false, false, false, false)]
    public void CapabilitiesFailClosedForReadOnlyAndRemote(
        NutManagementMode managementMode,
        ManagedNutServerAccessMode accessMode,
        bool canInspect,
        bool canEdit,
        bool canAdminister,
        bool canRunDiagnostics)
    {
        var profile = Profile("Profile", managementMode, managementMode == NutManagementMode.Remote ? "management" : null, "/etc/nut", accessMode);
        var capabilities = ManagedServerCapabilities.FromProfile(profile);

        Assert.True(capabilities.CanMonitor);
        Assert.Equal(canInspect, capabilities.CanInspectLocalManagement);
        Assert.Equal(canEdit, capabilities.CanEditConfiguration);
        Assert.Equal(canAdminister, capabilities.CanExecuteAdministrativeActions);
        Assert.Equal(canRunDiagnostics, capabilities.CanRunDriverDiagnostics);
        Assert.Equal(managementMode == NutManagementMode.Remote, capabilities.IsRemoteManagementAvailable);
    }

    [Fact]
    public async Task StoreRoundTripsUtf8SchemaAndNoCredentialFields()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonManagedNutServerProfileStore(directory.Path);
        var document = Document(Profile("Remote", NutManagementMode.Remote, "management.example", "/etc/nut"));

        await store.SaveAsync(document, CancellationToken.None);
        var json = await File.ReadAllTextAsync(store.ProfilesPath);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(document.SchemaVersion, loaded!.SchemaVersion);
        Assert.Equal(document.ActiveProfileId, loaded.ActiveProfileId);
        Assert.Equal(document.ActiveProfile, loaded.ActiveProfile);
        Assert.Contains("\"schemaVersion\": 2", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passphrase", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKeyPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task SchemaVersionOneRemoteProfileMigratesToSessionOnlySshMetadata()
    {
        using var directory = new TemporaryDirectory();
        var id = Guid.NewGuid();
        var store = new JsonManagedNutServerProfileStore(directory.Path);
        var legacy = $$"""{"schemaVersion":1,"activeProfileId":"{{id}}","profiles":[{"id":"{{id}}","name":"Remote","monitoringHost":"monitor.example","monitoringPort":3493,"managementMode":"Remote","managementHost":"management.example","remoteConfigurationDirectory":"/etc/nut","accessMode":"Manage"}]}""";
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(store.ProfilesPath, legacy);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(ManagedNutServerProfiles.CurrentSchemaVersion, loaded!.SchemaVersion);
        Assert.Equal(22, loaded.ActiveProfile.Management.SshPort);
        Assert.Null(loaded.ActiveProfile.Management.SshUsername);
        Assert.Null(loaded.ActiveProfile.Management.TrustedHostKeyFingerprint);
    }

    [Fact]
    public async Task StoreReturnsNullForMissingFileAndPreservesMalformedOrInvalidFiles()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonManagedNutServerProfileStore(directory.Path);
        Assert.Null(await store.LoadAsync(CancellationToken.None));

        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(store.ProfilesPath, "{ invalid");
        await Assert.ThrowsAsync<ManagedNutServerProfilePersistenceException>(() => store.LoadAsync(CancellationToken.None));
        Assert.Equal("{ invalid", await File.ReadAllTextAsync(store.ProfilesPath));

        var invalid = "{\"schemaVersion\":1,\"activeProfileId\":\"00000000-0000-0000-0000-000000000000\",\"profiles\":[]}";
        await File.WriteAllTextAsync(store.ProfilesPath, invalid);
        await Assert.ThrowsAsync<ManagedNutServerProfilePersistenceException>(() => store.LoadAsync(CancellationToken.None));
        Assert.Equal(invalid, await File.ReadAllTextAsync(store.ProfilesPath));
    }

    [Fact]
    public async Task StoreCancellationAndConcurrentSavesLeaveValidContent()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonManagedNutServerProfileStore(directory.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Document(Profile("Cancelled", NutManagementMode.Local)), cancellation.Token));
        Assert.False(File.Exists(store.ProfilesPath));

        var first = Document(Profile("First", NutManagementMode.Local));
        var second = Document(Profile("Second", NutManagementMode.Local));
        await Task.WhenAll(store.SaveAsync(first, CancellationToken.None), store.SaveAsync(second, CancellationToken.None));
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Contains(loaded!.ActiveProfile.Name, new[] { "First", "Second" });
    }

    [Fact]
    public async Task BootstrapMigratesLegacySettingsAndPreservesFallbackWhenPersistenceFails()
    {
        var settings = new ApplicationSettings(host: "legacy.example", port: 4444, preferredUpsName: "ups-a", mockMode: false);
        var savedStore = new RecordingProfileStore();
        var migrated = await new ManagedNutServerBootstrapper(savedStore).LoadAsync(settings, CancellationToken.None);

        Assert.True(migrated.WasMigrated);
        Assert.Null(migrated.Warning);
        Assert.Equal("legacy.example", migrated.RuntimeContext.Endpoint.Host);
        Assert.Equal(4444, migrated.RuntimeContext.Endpoint.Port);
        Assert.Equal("ups-a", migrated.RuntimeContext.Profile.Monitoring.PreferredUpsName);
        Assert.Equal(ManagedNutServerAccessMode.Manage, migrated.RuntimeContext.Profile.AccessMode);
        Assert.NotNull(savedStore.Saved);

        var fallback = await new ManagedNutServerBootstrapper(new FailingProfileStore()).LoadAsync(settings, CancellationToken.None);
        Assert.NotNull(fallback.Warning);
        Assert.Equal("legacy.example", fallback.RuntimeContext.Endpoint.Host);

        var malformed = new MalformedProfileStore();
        var malformedFallback = await new ManagedNutServerBootstrapper(malformed).LoadAsync(settings, CancellationToken.None);
        Assert.NotNull(malformedFallback.Warning);
        Assert.True(malformedFallback.IsProfileDocumentLoadFailure);
        Assert.Equal(0, malformed.SaveCalls);
    }

    [Fact]
    public async Task BootstrapUsesExistingActiveProfileInsteadOfLegacyEndpoint()
    {
        var local = Profile("Local", NutManagementMode.Local);
        var remote = new ManagedNutServerProfile(
            Guid.NewGuid(),
            "Remote",
            new NutMonitoringProfile("monitor.example", 3494, "remote-ups"),
            new NutManagementProfile(NutManagementMode.Remote, "management.example", "/etc/nut"),
            ManagedNutServerAccessMode.Manage);
        var profiles = new ManagedNutServerProfiles(1, remote.Id, [local, remote]);
        var result = await new ManagedNutServerBootstrapper(new RecordingProfileStore(profiles)).LoadAsync(new ApplicationSettings(host: "legacy", port: 3493), CancellationToken.None);

        Assert.Equal("monitor.example", result.RuntimeContext.Endpoint.Host);
        Assert.Equal(3494, result.RuntimeContext.Endpoint.Port);
        Assert.Equal("management.example", result.RuntimeContext.Profile.Management.ManagementHost);
        Assert.False(result.RuntimeContext.Capabilities.CanInspectLocalManagement);
    }

    private static ManagedNutServerProfile Profile(
        string name,
        NutManagementMode mode,
        string? managementHost = null,
        string? remoteDirectory = null,
        ManagedNutServerAccessMode accessMode = ManagedNutServerAccessMode.Manage) => new(
        Guid.NewGuid(),
        name,
        new NutMonitoringProfile("monitor.example", 3493, "ups-a"),
        new NutManagementProfile(mode, managementHost, remoteDirectory),
        accessMode);

    private static ManagedNutServerProfiles Document(ManagedNutServerProfile profile) => new(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]);

    private sealed class RecordingProfileStore : IManagedNutServerProfileStore
    {
        private readonly ManagedNutServerProfiles? _load;

        public RecordingProfileStore(ManagedNutServerProfiles? load = null) => _load = load;

        public ManagedNutServerProfiles? Saved { get; private set; }

        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_load);

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

    private sealed class MalformedProfileStore : IManagedNutServerProfileStore
    {
        public int SaveCalls { get; private set; }

        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) => throw new ManagedNutServerProfilePersistenceException("malformed");

        public Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NutManager.Tests.{Guid.NewGuid():N}");

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
