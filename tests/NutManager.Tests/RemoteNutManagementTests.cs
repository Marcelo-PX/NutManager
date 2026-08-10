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
        Assert.True(SshHostKeyFingerprint.Matches(fingerprint, hostKey));
        Assert.False(SshHostKeyFingerprint.Matches(null, hostKey));
        Assert.False(SshHostKeyFingerprint.Matches("SHA256:YWJjZA==", hostKey));
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
    }

    [Fact]
    public void SessionOnlyAuthenticationDoesNotExposeCredentialsInPersistedProfileModel()
    {
        var profile = new NutManagementProfile(
            NutManagementMode.Remote,
            "management.example",
            "/etc/nut",
            sshPort: 2222,
            sshUsername: "nutadmin",
            trustedHostKeyFingerprint: "SHA256:YWJjZA==",
            trustedHostKeyAlgorithm: "ssh-ed25519");

        Assert.Equal(2222, profile.SshPort);
        Assert.Equal("nutadmin", profile.SshUsername);
        Assert.Equal("SHA256:YWJjZA==", profile.TrustedHostKeyFingerprint);
        Assert.DoesNotContain(typeof(NutManagementProfile).GetProperties(), property =>
            property.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("privatekey", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeRemoteSession : IRemoteNutManagementSession
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public FakeRemoteSession(RemoteNutPlatform platform) => Platform = platform;

        public RemoteNutPlatform Platform { get; }
        public string HomeDirectory => "/home/nut";
        public int CommitCalls { get; private set; }

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
            var path = RemotePathMapper.Combine(request.ConfigurationDirectory, request.TemporaryFileName);
            _files[path] = request.CandidateBytes.ToArray();
            return Task.FromResult(new RemoteNutFileReadResult(RemoteNutTransportStatus.Success, _files[path]));
        }

        public Task<RemoteNutCommitResult> CommitWindowsConfigurationAsync(RemoteNutWindowsCommitRequest request, CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            var target = RemotePathMapper.Combine(request.ConfigurationDirectory, request.TargetFileName);
            var temporary = RemotePathMapper.Combine(request.ConfigurationDirectory, request.TemporaryFileName);
            var backup = RemotePathMapper.Combine(request.ConfigurationDirectory, request.BackupFileName);
            _files[backup] = _files[target];
            _files[target] = _files[temporary];
            _files.Remove(temporary);
            return Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Success, backup));
        }

        public Task<RemoteNutCommitResult> RollbackWindowsConfigurationAsync(RemoteNutWindowsRollbackRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Failed));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
