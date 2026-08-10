using System.Text;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Configuration;
using NutManager.Infrastructure.Remote.Ssh;
using Xunit;

namespace NutManager.Tests;

public sealed class RemoteNutManagementTests
{
    private const string CanonicalFingerprint = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Theory]
    [InlineData("/etc/nut", "/etc/nut")]
    [InlineData("C:\\NUT\\etc", "C:/NUT/etc")]
    [InlineData("C:/NUT/etc", "C:/NUT/etc")]
    public void RemotePathsAreNormalizedWithoutHostLocalPathSemantics(string input, string expected) =>
        Assert.Equal(expected, RemotePathMapper.ToSftpPath(input));

    [Theory]
    [InlineData("../etc/nut")]
    [InlineData("C:\\NUT-malicious\\..\\etc")]
    [InlineData("relative/path")]
    public void UnsafeRemotePathsAreRejected(string input) =>
        Assert.Throws<ArgumentException>(() => RemotePathMapper.ToSftpPath(input));

    [Fact]
    public void FixedWindowsCommitCommandContainsOnlyStructuredPayload()
    {
        var request = new RemoteNutWindowsCommitRequest("C:/NUT/etc", "ups.conf", ".nutmanager-a.tmp", ".nutmanager-a.bak", "A", "B");
        var command = RemoteWindowsCommandBuilder.BuildWindowsCommit(request);

        Assert.StartsWith("powershell.exe -NoProfile -NonInteractive -EncodedCommand ", command, StringComparison.Ordinal);
        Assert.DoesNotContain("cmd.exe", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upsdrvctl", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostKeyFingerprintRequiresExactPinnedSha256Value()
    {
        var hostKey = Encoding.UTF8.GetBytes("fictional-host-key");
        var fingerprint = SshHostKeyFingerprint.Create(hostKey);

        Assert.StartsWith("SHA256:", fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain('=', fingerprint);
        Assert.True(SshHostKeyFingerprint.Matches(fingerprint, hostKey));
        Assert.False(SshHostKeyFingerprint.Matches(null, hostKey));
        Assert.False(SshHostKeyFingerprint.Matches(CanonicalFingerprint, hostKey));
        var modified = fingerprint[..^1] + (fingerprint[^1] == 'A' ? "B" : "A");
        Assert.False(SshHostKeyFingerprint.Matches(modified, hostKey));
    }

    [Fact]
    public async Task RemotePipelineUsesRemoteSessionForLoadPrepareAndWindowsSafeCommit()
    {
        var session = new FakeRemoteSession(RemoteNutPlatform.Windows);
        session.SetFile("/etc/nut/nut.conf", "MODE=standalone\n");
        var pipeline = new RemoteNutConfigurationFilePipeline(session, "/etc/nut", true);

        var load = await pipeline.LoadAsync("/etc/nut/nut.conf", NutConfigurationFileKind.NutConf);
        var snapshot = Assert.IsType<NutConfigurationFileSnapshot>(load.Snapshot);
        Assert.IsType<NutConfigurationAssignmentNode>(snapshot.Document.Nodes.Single()).SetValue("netserver");
        var prepared = pipeline.Prepare(snapshot);

        var applied = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.Success, applied.Status);
        Assert.NotNull(applied.BackupPath);
        Assert.Equal("MODE=netserver\n", session.GetText("/etc/nut/nut.conf"));
        Assert.Equal("MODE=standalone\n", session.GetText(applied.BackupPath!));
        Assert.Equal(1, session.CommitCalls);
        Assert.Equal(0, session.CleanupCalls);
    }

    [Fact]
    public async Task RemotePipelineStaysReadOnlyWithoutVerifiedWindowsCapability()
    {
        var session = new FakeRemoteSession(RemoteNutPlatform.NonWindows);
        session.SetFile("/etc/nut/nut.conf", "MODE=standalone\n");
        var pipeline = new RemoteNutConfigurationFilePipeline(session, "/etc/nut", false);
        var load = await pipeline.LoadAsync("/etc/nut/nut.conf", NutConfigurationFileKind.NutConf);
        var snapshot = Assert.IsType<NutConfigurationFileSnapshot>(load.Snapshot);
        Assert.IsType<NutConfigurationAssignmentNode>(snapshot.Document.Nodes.Single()).SetValue("netserver");

        var applied = await pipeline.ApplyAsync(pipeline.Prepare(snapshot));

        Assert.Equal(NutConfigurationApplyStatus.Failed, applied.Status);
        Assert.Equal(0, session.CommitCalls);
        Assert.Equal(0, session.UploadCalls);
    }

    [Fact]
    public async Task CandidateVerificationFailureCleansGeneratedTemporaryFile()
    {
        var session = NewWritableSession();
        session.UploadBytesOverride = Encoding.UTF8.GetBytes("invalid candidate");
        var applied = await ApplyChangedNutConfAsync(session);

        Assert.Equal(NutConfigurationApplyStatus.TempWriteFailed, applied.Status);
        Assert.Equal(1, session.CleanupCalls);
        Assert.DoesNotContain(session.FilePaths, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CandidateVerificationCleanupFailureIsCriticalAndExposesTemporaryPath()
    {
        var session = NewWritableSession();
        session.UploadBytesOverride = Encoding.UTF8.GetBytes("invalid candidate");
        session.CleanupStatus = RemoteNutTransportStatus.AccessDenied;

        var applied = await ApplyChangedNutConfAsync(session);

        Assert.Equal(NutConfigurationApplyStatus.RemoteTemporaryCleanupFailed, applied.Status);
        Assert.NotNull(applied.TemporaryPath);
        Assert.Equal(1, session.CleanupCalls);
    }

    [Fact]
    public async Task ExternalChangeAfterUploadCleansCandidate()
    {
        var session = NewWritableSession();
        session.AfterUpload = () => session.SetFile("/etc/nut/nut.conf", "MODE=external\n");

        var applied = await ApplyChangedNutConfAsync(session);

        Assert.Equal(NutConfigurationApplyStatus.ChangedExternally, applied.Status);
        Assert.Equal(1, session.CleanupCalls);
        Assert.DoesNotContain(session.FilePaths, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExternalChangeCleanupFailureExposesTemporaryPath()
    {
        var session = NewWritableSession();
        session.AfterUpload = () => session.SetFile("/etc/nut/nut.conf", "MODE=external\n");
        session.CleanupStatus = RemoteNutTransportStatus.Timeout;

        var applied = await ApplyChangedNutConfAsync(session);

        Assert.Equal(NutConfigurationApplyStatus.RemoteTemporaryCleanupFailed, applied.Status);
        Assert.NotNull(applied.TemporaryPath);
        Assert.Equal(1, session.CleanupCalls);
    }

    [Fact]
    public async Task KnownCommitRejectionCleansCandidateButOutcomeUnknownDoesNot()
    {
        var rejected = NewWritableSession();
        rejected.CommitStatus = RemoteNutTransportStatus.Failed;
        var rejection = await ApplyChangedNutConfAsync(rejected);

        Assert.Equal(NutConfigurationApplyStatus.ReplaceFailed, rejection.Status);
        Assert.Equal(1, rejected.CleanupCalls);

        var unknown = NewWritableSession();
        unknown.CommitStatus = RemoteNutTransportStatus.OutcomeUnknown;
        var unknownResult = await ApplyChangedNutConfAsync(unknown);

        Assert.Equal(NutConfigurationApplyStatus.RemoteCommitOutcomeUnknown, unknownResult.Status);
        Assert.Equal(0, unknown.CleanupCalls);
        Assert.NotNull(unknownResult.TemporaryPath);
        Assert.Contains(unknown.FilePaths, path => string.Equals(path, unknownResult.TemporaryPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerificationFailureRollbackRestoresOriginalAndBackupMismatchNeverReportsSuccess()
    {
        var rollback = NewWritableSession();
        rollback.ForceVerificationFailure = true;
        rollback.RollbackStatus = RemoteNutTransportStatus.Success;

        var rolledBack = await ApplyChangedNutConfAsync(rollback);

        Assert.Equal(NutConfigurationApplyStatus.VerificationFailedRolledBack, rolledBack.Status);
        Assert.Equal("MODE=standalone\n", rollback.GetText("/etc/nut/nut.conf"));
        Assert.Equal(1, rollback.RollbackCalls);

        var backupMismatch = NewWritableSession();
        backupMismatch.SkipBackup = true;
        var mismatch = await ApplyChangedNutConfAsync(backupMismatch);

        Assert.NotEqual(NutConfigurationApplyStatus.Success, mismatch.Status);
    }

    [Theory]
    [InlineData("nut.conf")]
    [InlineData(".nutmanager-unsafe.bak")]
    [InlineData(".nutmanager-.tmp")]
    [InlineData(".nutmanager-unsafe.tmp/child")]
    [InlineData(".nutmanager-../unsafe.tmp")]
    public void RestrictedTemporaryCleanupRejectsUnsafeNames(string name) =>
        Assert.False(RemoteNutGeneratedTemporaryFile.IsValidName(name));

    [Fact]
    public void RestrictedTemporaryCleanupAcceptsOnlyGeneratedDirectChild() =>
        Assert.True(RemoteNutGeneratedTemporaryFile.IsValidName(".nutmanager-nut.conf-abc.tmp"));

    [Fact]
    public void SessionOnlyAuthenticationDoesNotExposeCredentialsInPersistedProfileModel()
    {
        var profile = new NutManagementProfile(
            NutManagementMode.Remote,
            "management.example",
            "/etc/nut",
            sshPort: 2222,
            sshUsername: "nutadmin",
            trustedHostKeyFingerprint: CanonicalFingerprint,
            trustedHostKeyAlgorithm: "ssh-ed25519");

        Assert.Equal(2222, profile.SshPort);
        Assert.Equal("nutadmin", profile.SshUsername);
        Assert.Equal(CanonicalFingerprint, profile.TrustedHostKeyFingerprint);
        Assert.DoesNotContain(typeof(NutManagementProfile).GetProperties(), property =>
            property.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("privatekey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedHostKeyFingerprintMustUseCanonicalUnpaddedSha256Format()
    {
        Assert.Throws<ArgumentException>(() => new NutManagementProfile(
            NutManagementMode.Remote,
            "management.example",
            "/etc/nut",
            sshUsername: "nutadmin",
            trustedHostKeyFingerprint: CanonicalFingerprint + "="));

        var profile = new NutManagementProfile(
            NutManagementMode.Remote,
            "management.example",
            "/etc/nut",
            sshUsername: "nutadmin",
            trustedHostKeyFingerprint: CanonicalFingerprint);
        Assert.Equal(CanonicalFingerprint, profile.TrustedHostKeyFingerprint);
    }

    private static FakeRemoteSession NewWritableSession()
    {
        var session = new FakeRemoteSession(RemoteNutPlatform.Windows);
        session.SetFile("/etc/nut/nut.conf", "MODE=standalone\n");
        return session;
    }

    private static async Task<NutConfigurationApplyResult> ApplyChangedNutConfAsync(FakeRemoteSession session)
    {
        var pipeline = new RemoteNutConfigurationFilePipeline(session, "/etc/nut", true);
        var load = await pipeline.LoadAsync("/etc/nut/nut.conf", NutConfigurationFileKind.NutConf);
        var snapshot = Assert.IsType<NutConfigurationFileSnapshot>(load.Snapshot);
        Assert.IsType<NutConfigurationAssignmentNode>(snapshot.Document.Nodes.Single()).SetValue("netserver");
        return await pipeline.ApplyAsync(pipeline.Prepare(snapshot));
    }

    private sealed class FakeRemoteSession : IRemoteNutManagementSession
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public FakeRemoteSession(RemoteNutPlatform platform) => Platform = platform;

        public RemoteNutPlatform Platform { get; }
        public string HomeDirectory => "/home/nut";
        public int CommitCalls { get; private set; }
        public int UploadCalls { get; private set; }
        public int CleanupCalls { get; private set; }
        public RemoteNutTransportStatus CleanupStatus { get; set; } = RemoteNutTransportStatus.Success;
        public RemoteNutTransportStatus? CommitStatus { get; set; }
        public RemoteNutTransportStatus RollbackStatus { get; set; } = RemoteNutTransportStatus.Failed;
        public byte[]? UploadBytesOverride { get; set; }
        public Action? AfterUpload { get; set; }
        public bool ForceVerificationFailure { get; set; }
        public bool SkipBackup { get; set; }
        public IReadOnlyCollection<string> FilePaths => _files.Keys;
        public int RollbackCalls { get; private set; }

        public void SetFile(string path, string text) => _files[path] = Encoding.UTF8.GetBytes(text);
        public string GetText(string path) => Encoding.UTF8.GetString(_files[path]);

        public Task<RemoteNutDirectoryListing> BrowseDirectoryAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutDirectoryListing(directory, "/", []));

        public Task<RemoteNutDirectoryValidationResult> ValidateConfigurationDirectoryAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.Success, directory, RemoteNutConfigurationFiles.AllNames));

        public Task<RemoteNutFileReadResult> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(_files.TryGetValue(path, out var bytes)
                ? new RemoteNutFileReadResult(RemoteNutTransportStatus.Success, bytes)
                : new RemoteNutFileReadResult(RemoteNutTransportStatus.NotFound));

        public Task<RemoteNutWriteCapabilityResult> ProbeSafeWriteCapabilityAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutWriteCapabilityResult(true, Platform));

        public Task<RemoteNutFileReadResult> UploadCandidateAsync(RemoteNutCandidateUploadRequest request, CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            var path = RemotePathMapper.Combine(request.ConfigurationDirectory, request.TemporaryFileName);
            _files[path] = request.CandidateBytes.ToArray();
            AfterUpload?.Invoke();
            return Task.FromResult(new RemoteNutFileReadResult(RemoteNutTransportStatus.Success, UploadBytesOverride ?? _files[path]));
        }

        public Task<RemoteNutTemporaryCleanupResult> DeleteGeneratedTemporaryFileAsync(string configurationDirectory, string temporaryFileName, CancellationToken cancellationToken = default)
        {
            if (!RemoteNutGeneratedTemporaryFile.IsValidName(temporaryFileName))
            {
                return Task.FromResult(new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.InvalidPath));
            }

            CleanupCalls++;
            var path = RemotePathMapper.Combine(configurationDirectory, temporaryFileName);
            if (CleanupStatus is not (RemoteNutTransportStatus.Success or RemoteNutTransportStatus.NotFound))
            {
                return Task.FromResult(new RemoteNutTemporaryCleanupResult(CleanupStatus));
            }

            return Task.FromResult(_files.Remove(path)
                ? new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.Success)
                : new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.NotFound));
        }

        public Task<RemoteNutCommitResult> CommitWindowsConfigurationAsync(RemoteNutWindowsCommitRequest request, CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            if (CommitStatus is { } status)
            {
                return Task.FromResult(new RemoteNutCommitResult(status));
            }
            var target = RemotePathMapper.Combine(request.ConfigurationDirectory, request.TargetFileName);
            var temporary = RemotePathMapper.Combine(request.ConfigurationDirectory, request.TemporaryFileName);
            var backup = RemotePathMapper.Combine(request.ConfigurationDirectory, request.BackupFileName);
            if (!SkipBackup)
            {
                _files[backup] = _files[target];
            }
            _files[target] = _files[temporary];
            _files.Remove(temporary);
            if (ForceVerificationFailure)
            {
                _files[target] = Encoding.UTF8.GetBytes("MODE=unexpected\n");
            }
            return Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Success, backup));
        }

        public Task<RemoteNutCommitResult> RollbackWindowsConfigurationAsync(RemoteNutWindowsRollbackRequest request, CancellationToken cancellationToken = default)
        {
            RollbackCalls++;
            if (RollbackStatus != RemoteNutTransportStatus.Success)
            {
                return Task.FromResult(new RemoteNutCommitResult(RollbackStatus));
            }

            var target = RemotePathMapper.Combine(request.ConfigurationDirectory, request.TargetFileName);
            var backup = RemotePathMapper.Combine(request.ConfigurationDirectory, request.BackupFileName);
            var recovery = RemotePathMapper.Combine(request.ConfigurationDirectory, request.RecoveryFileName);
            _files[recovery] = _files[target];
            _files[target] = _files[backup];
            return Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Success, recoveryPath: recovery));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
