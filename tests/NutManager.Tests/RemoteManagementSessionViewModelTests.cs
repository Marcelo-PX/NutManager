using NutManager.App.Services;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using Xunit;

namespace NutManager.Tests;

public sealed class RemoteManagementSessionViewModelTests
{
    private const string CanonicalFingerprint = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task UnknownHostKeyRequiresExplicitTrustAndPersistsOnlyFingerprintMetadata()
    {
        var profile = RemoteProfile(ManagedNutServerAccessMode.Manage);
        var store = new RecordingStore(new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]));
        var transport = new FakeTransport(new RemoteNutConnectionResult(
            RemoteNutConnectionState.HostKeyTrustRequired,
            hostKey: new RemoteNutHostKeyInfo("management.example", 22, "ssh-ed25519", CanonicalFingerprint)));
        var viewModel = new RemoteManagementSessionViewModel(profile, transport, new ManagedNutServerProfileUpdateService(store));

        await viewModel.ConnectWithPasswordAsync("fictional-password".AsMemory());

        Assert.Equal(RemoteNutConnectionState.HostKeyTrustRequired, viewModel.ConnectionState);
        Assert.True(viewModel.CanTrustHostKey);
        Assert.Equal("ssh-ed25519", viewModel.PresentedHostKey!.Algorithm);
        Assert.Equal(CanonicalFingerprint, viewModel.PresentedHostKey.Fingerprint);
        await viewModel.TrustPresentedHostKeyAsync();

        Assert.Equal(CanonicalFingerprint, viewModel.TrustedHostKeyFingerprint);
        Assert.NotNull(store.Saved);
        Assert.Equal(CanonicalFingerprint, store.Saved!.ActiveProfile.Management.TrustedHostKeyFingerprint);
        Assert.DoesNotContain("fictional-password", store.Saved.ActiveProfile.Management.TrustedHostKeyFingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadOnlyRemoteSessionCanValidateAndReadButCannotProbeOrEdit()
    {
        var profile = RemoteProfile(ManagedNutServerAccessMode.ReadOnly);
        var session = new FakeSession(RemoteNutPlatform.Windows);
        var viewModel = new RemoteManagementSessionViewModel(profile, new FakeTransport(new RemoteNutConnectionResult(RemoteNutConnectionState.Connected, session)));

        await viewModel.ConnectWithPasswordAsync("fictional-password".AsMemory());
        await viewModel.ValidateCurrentDirectoryAsync();

        Assert.True(viewModel.CanReadConfiguration);
        Assert.False(viewModel.CanProbeWriteCapability);
        Assert.False(viewModel.CanEditConfiguration);
    }

    [Fact]
    public async Task HostKeyMismatchNeverPersistsThePresentedKey()
    {
        var profile = RemoteProfile(ManagedNutServerAccessMode.Manage);
        var store = new RecordingStore(new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]));
        var transport = new FakeTransport(new RemoteNutConnectionResult(
            RemoteNutConnectionState.HostKeyMismatch,
            hostKey: new RemoteNutHostKeyInfo("management.example", 22, "ssh-ed25519", CanonicalFingerprint)));
        var viewModel = new RemoteManagementSessionViewModel(profile, transport, new ManagedNutServerProfileUpdateService(store));

        await viewModel.ConnectWithPasswordAsync("fictional-password".AsMemory());

        Assert.Equal(RemoteNutConnectionState.HostKeyMismatch, viewModel.ConnectionState);
        Assert.False(viewModel.CanTrustHostKey);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task ManageRemoteSessionRequiresExplicitWindowsCapabilityProbeBeforeEditing()
    {
        var profile = RemoteProfile(ManagedNutServerAccessMode.Manage);
        var session = new FakeSession(RemoteNutPlatform.Windows);
        var viewModel = new RemoteManagementSessionViewModel(profile, new FakeTransport(new RemoteNutConnectionResult(RemoteNutConnectionState.Connected, session)));
        INutConfigurationFilePipeline? configuredPipeline = null;
        viewModel.ConfigurationContextChanged += (pipeline, _, _) => configuredPipeline = pipeline;

        await viewModel.ConnectWithPasswordAsync("fictional-password".AsMemory());
        await viewModel.ValidateCurrentDirectoryAsync();
        Assert.True(viewModel.CanReadConfiguration);
        Assert.False(viewModel.CanEditConfiguration);

        await viewModel.ProbeWriteCapabilityAsync();

        Assert.True(viewModel.CanEditConfiguration);
        Assert.NotNull(configuredPipeline);
        Assert.Equal(1, session.ProbeCalls);
    }

    [Fact]
    public async Task CapabilityProbeCleanupFailureBlocksEditingAndIsCritical()
    {
        var profile = RemoteProfile(ManagedNutServerAccessMode.Manage);
        var session = new FakeSession(RemoteNutPlatform.Windows)
        {
            ProbeResult = new RemoteNutWriteCapabilityResult(false, RemoteNutPlatform.Windows, "/etc/nut/.nutmanager-probe.tmp", "cleanup failed")
        };
        var viewModel = new RemoteManagementSessionViewModel(profile, new FakeTransport(new RemoteNutConnectionResult(RemoteNutConnectionState.Connected, session)));

        await viewModel.ConnectWithPasswordAsync("fictional-password".AsMemory());
        await viewModel.ValidateCurrentDirectoryAsync();
        await viewModel.ProbeWriteCapabilityAsync();

        Assert.True(viewModel.IsWriteCapabilityCritical);
        Assert.False(viewModel.CanEditConfiguration);
        Assert.Contains("CRÍTICO", viewModel.WriteCapabilityCriticalText);
    }

    [Fact]
    public async Task IndeterminateRemoteWriteDisablesEditingUntilReconnectAndProbe()
    {
        var profile = RemoteProfile(ManagedNutServerAccessMode.Manage);
        var session = new FakeSession(RemoteNutPlatform.Windows);
        var viewModel = new RemoteManagementSessionViewModel(profile, new FakeTransport(new RemoteNutConnectionResult(RemoteNutConnectionState.Connected, session)));

        await viewModel.ConnectWithPasswordAsync("fictional-password".AsMemory());
        await viewModel.ValidateCurrentDirectoryAsync();
        await viewModel.ProbeWriteCapabilityAsync();
        Assert.True(viewModel.CanEditConfiguration);

        viewModel.InvalidateWriteCapabilityAfterUncertainOutcome();

        Assert.False(viewModel.CanEditConfiguration);
        Assert.Contains("conecte novamente", viewModel.WriteCapabilityText);
    }

    [Fact]
    public async Task EditingRemoteDirectoryTextInvalidatesThePreviouslyValidatedContext()
    {
        var profile = RemoteProfile(ManagedNutServerAccessMode.ReadOnly);
        var session = new FakeSession(RemoteNutPlatform.Windows);
        var viewModel = new RemoteManagementSessionViewModel(profile, new FakeTransport(new RemoteNutConnectionResult(RemoteNutConnectionState.Connected, session)));

        await viewModel.ConnectWithPasswordAsync("fictional-password".AsMemory());
        await viewModel.ValidateCurrentDirectoryAsync();
        Assert.True(viewModel.CanReadConfiguration);

        viewModel.CurrentDirectory = "/other/nut";

        Assert.False(viewModel.IsDirectoryValidated);
        Assert.False(viewModel.CanReadConfiguration);
        Assert.False(viewModel.CanUseCurrentDirectory);
    }

    private static ManagedNutServerProfile RemoteProfile(ManagedNutServerAccessMode accessMode) => new(
        Guid.NewGuid(),
        "Remote",
        new NutMonitoringProfile("monitor.example"),
        new NutManagementProfile(NutManagementMode.Remote, "management.example", "/etc/nut", sshUsername: "nutadmin"),
        accessMode);

    private sealed class RecordingStore : IManagedNutServerProfileStore
    {
        private readonly ManagedNutServerProfiles _loaded;
        public RecordingStore(ManagedNutServerProfiles loaded) => _loaded = loaded;
        public ManagedNutServerProfiles? Saved { get; private set; }
        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ManagedNutServerProfiles?>(Saved ?? _loaded);
        public Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken)
        {
            Saved = profiles;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransport : IRemoteNutManagementTransport
    {
        private readonly RemoteNutConnectionResult _result;
        public FakeTransport(RemoteNutConnectionResult result) => _result = result;
        public Task<RemoteNutConnectionResult> ConnectAsync(RemoteNutConnectionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(_result);
    }

    private sealed class FakeSession : IRemoteNutManagementSession
    {
        public FakeSession(RemoteNutPlatform platform) => Platform = platform;
        public RemoteNutPlatform Platform { get; }
        public bool IsSafeWriteCapabilityValid => true;
        public string HomeDirectory => "/etc/nut";
        public int ProbeCalls { get; private set; }
        public RemoteNutWriteCapabilityResult? ProbeResult { get; init; }
        public Task<RemoteNutDirectoryListing> BrowseDirectoryAsync(string directory, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteNutDirectoryListing(directory, "/etc", []));
        public Task<RemoteNutDirectoryValidationResult> ValidateConfigurationDirectoryAsync(string directory, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.Success, directory, ["nut.conf"]));
        public Task<RemoteNutFileReadResult> ReadFileAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteNutFileReadResult(RemoteNutTransportStatus.NotFound));
        public Task<RemoteNutWriteCapabilityResult> ProbeSafeWriteCapabilityAsync(string directory, CancellationToken cancellationToken = default)
        {
            ProbeCalls++;
            return Task.FromResult(ProbeResult ?? new RemoteNutWriteCapabilityResult(true, Platform));
        }
        public void InvalidateSafeWriteCapability() { }
        public Task<RemoteNutFileReadResult> UploadCandidateAsync(RemoteNutCandidateUploadRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteNutFileReadResult(RemoteNutTransportStatus.Unsupported));
        public Task<RemoteNutTemporaryCleanupResult> DeleteGeneratedTemporaryFileAsync(string configurationDirectory, string temporaryFileName, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.NotFound));
        public Task<RemoteNutCommitResult> CommitWindowsConfigurationAsync(RemoteNutWindowsCommitRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Unsupported));
        public Task<RemoteNutCommitResult> RollbackWindowsConfigurationAsync(RemoteNutWindowsRollbackRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Unsupported));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
