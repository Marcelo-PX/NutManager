using System.Text;
using System.Security.Cryptography;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Configuration;
using NutManager.Infrastructure.Remote.Smb;
using Xunit;

namespace NutManager.Tests;

public sealed class WindowsSmbRemoteNutConfigurationTests
{
    private const string Share = @"\\server\share";
    private const string ConfigurationDirectory = @"\\server\share\NUT\etc";

    [Theory]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData(@"\\SERVER\Share\NUT\etc", @"\\SERVER\Share\NUT\etc")]
    public void SmbUncPathsNormalizeWithoutHostFilesystemSemantics(string input, string expected) =>
        Assert.Equal(expected, SmbUncPath.NormalizeUncPath(input));

    [Theory]
    [InlineData(@"C:\NUT\etc")]
    [InlineData(@"\\server")]
    [InlineData(@"relative\share")]
    [InlineData(@"\\server\share\..\other")]
    public void InvalidSmbShareRootsAreRejected(string path) =>
        Assert.Throws<ArgumentException>(() => SmbUncPath.NormalizeShareRoot(path));

    [Fact]
    public void SmbConfigurationDirectoryCannotEscapeItsConfiguredShare()
    {
        Assert.Equal(ConfigurationDirectory, SmbUncPath.NormalizeConfigurationDirectory(Share, ConfigurationDirectory));
        Assert.Throws<ArgumentException>(() => SmbUncPath.NormalizeConfigurationDirectory(Share, @"\\other\share\NUT\etc"));
        Assert.False(SmbUncPath.IsWithinShare(Share, @"\\server\other\NUT\etc"));
    }

    [Fact]
    public async Task CurrentWindowsIdentityDoesNotCreateOrCancelWnetConnection()
    {
        var network = new FakeNetworkConnection();
        var transport = new WindowsSmbRemoteNutConfigurationTransport(new FakeSmbFileSystem(), network, () => true);
        var request = new SmbRemoteNutConnectionRequest(Guid.NewGuid(), Share, SmbAuthenticationMode.CurrentWindowsIdentity, null, default, true);

        var result = await transport.ConnectAsync(request);

        Assert.Equal(RemoteNutConnectionState.Connected, result.State);
        Assert.Equal(0, network.ConnectCalls);
        await result.Session!.DisposeAsync();
        Assert.Equal(0, network.DisconnectCalls);
    }

    [Fact]
    public async Task ExplicitSmbCredentialsCreateAndDisposeOnlyTheOwnedWnetConnection()
    {
        var network = new FakeNetworkConnection();
        var transport = new WindowsSmbRemoteNutConfigurationTransport(new FakeSmbFileSystem(), network, () => true);
        var request = new SmbRemoteNutConnectionRequest(Guid.NewGuid(), Share, SmbAuthenticationMode.ExplicitCredentials, "DOMAIN\\nut", "fictional-password".AsMemory(), true);

        var result = await transport.ConnectAsync(request);

        Assert.Equal(RemoteNutConnectionState.Connected, result.State);
        Assert.Equal(1, network.ConnectCalls);
        Assert.Equal(Share, network.LastShare);
        Assert.Equal("DOMAIN\\nut", network.LastUsername);
        Assert.DoesNotContain("fictional-password", result.Message ?? string.Empty, StringComparison.Ordinal);
        await result.Session!.DisposeAsync();
        Assert.Equal(1, network.DisconnectCalls);
        Assert.False(network.LastDisconnectForce);
    }

    [Fact]
    public async Task CredentialConflictFailsClosedWithoutDisconnectingAnotherWindowsSession()
    {
        var network = new FakeNetworkConnection { ConnectResult = new WindowsNetworkConnectionResult(WindowsNetworkConnectionResult.CredentialConflict) };
        var transport = new WindowsSmbRemoteNutConfigurationTransport(new FakeSmbFileSystem(), network, () => true);
        var request = new SmbRemoteNutConnectionRequest(Guid.NewGuid(), Share, SmbAuthenticationMode.ExplicitCredentials, "DOMAIN\\nut", "fictional-password".AsMemory(), true);

        var result = await transport.ConnectAsync(request);

        Assert.Equal(RemoteNutConnectionState.AuthenticationFailed, result.State);
        Assert.Contains("outras credenciais", result.Message, StringComparison.Ordinal);
        Assert.Null(result.Session);
        Assert.Equal(0, network.DisconnectCalls);
    }

    [Fact]
    public async Task SafeWriteProbeBindsOnlyItsExactValidatedSmbDirectory()
    {
        var fileSystem = new FakeSmbFileSystem();
        var session = CreateSession(fileSystem);
        await session.ValidateConfigurationDirectoryAsync(ConfigurationDirectory);

        var probe = await session.ProbeSafeWriteCapabilityAsync(ConfigurationDirectory);

        Assert.True(probe.IsSupported);
        Assert.True(session.IsSafeWriteCapabilityValidFor(ConfigurationDirectory));
        Assert.False(session.IsSafeWriteCapabilityValidFor(@"\\server\share\other"));
        Assert.Equal(1, fileSystem.ReplaceCalls);
        Assert.DoesNotContain(fileSystem.FilePaths, path => path.Contains("capability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProbeFailureOrCleanupFailureDoesNotEnableSmbWrites()
    {
        var unsupported = new FakeSmbFileSystem { ReplaceThrows = true };
        var unsupportedSession = CreateSession(unsupported);
        await unsupportedSession.ValidateConfigurationDirectoryAsync(ConfigurationDirectory);
        Assert.False((await unsupportedSession.ProbeSafeWriteCapabilityAsync(ConfigurationDirectory)).IsSupported);
        Assert.False(unsupportedSession.IsSafeWriteCapabilityValidFor(ConfigurationDirectory));

        var cleanupFailure = new FakeSmbFileSystem { FailCapabilityCleanup = true };
        var cleanupSession = CreateSession(cleanupFailure);
        await cleanupSession.ValidateConfigurationDirectoryAsync(ConfigurationDirectory);
        var cleanup = await cleanupSession.ProbeSafeWriteCapabilityAsync(ConfigurationDirectory);
        Assert.False(cleanup.IsSupported);
        Assert.NotNull(cleanup.CleanupPath);
        Assert.False(cleanupSession.IsSafeWriteCapabilityValidFor(ConfigurationDirectory));
    }

    [Fact]
    public async Task ManageProfileCannotCreateCandidateBeforeSafeWriteProbe()
    {
        var fileSystem = new FakeSmbFileSystem();
        fileSystem.SetFile(SmbUncPath.CombineDirectChild(ConfigurationDirectory, "nut.conf"), "MODE=standalone\n");
        var session = CreateSession(fileSystem);
        await session.ValidateConfigurationDirectoryAsync(ConfigurationDirectory);
        var pipeline = new RemoteNutConfigurationFilePipeline(session, ConfigurationDirectory, true);
        var load = await pipeline.LoadAsync(SmbUncPath.CombineDirectChild(ConfigurationDirectory, "nut.conf"), NutConfigurationFileKind.NutConf);
        var snapshot = Assert.IsType<NutConfigurationFileSnapshot>(load.Snapshot);
        Assert.IsType<NutConfigurationAssignmentNode>(snapshot.Document.Nodes.Single()).SetValue("netserver");

        var result = await pipeline.ApplyAsync(pipeline.Prepare(snapshot));

        Assert.Equal(NutConfigurationApplyStatus.Failed, result.Status);
        Assert.Equal(0, fileSystem.ReplaceCalls);
        Assert.DoesNotContain(fileSystem.FilePaths, path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SmbPipelineUsesCreateNewReplaceBackupAndVerification()
    {
        var fileSystem = new FakeSmbFileSystem();
        var targetPath = SmbUncPath.CombineDirectChild(ConfigurationDirectory, "nut.conf");
        fileSystem.SetFile(targetPath, "MODE=standalone\n");
        var session = CreateSession(fileSystem);
        await session.ValidateConfigurationDirectoryAsync(ConfigurationDirectory);
        Assert.True((await session.ProbeSafeWriteCapabilityAsync(ConfigurationDirectory)).IsSupported);
        var pipeline = new RemoteNutConfigurationFilePipeline(session, ConfigurationDirectory, true);
        var load = await pipeline.LoadAsync(targetPath, NutConfigurationFileKind.NutConf);
        var snapshot = Assert.IsType<NutConfigurationFileSnapshot>(load.Snapshot);
        Assert.IsType<NutConfigurationAssignmentNode>(snapshot.Document.Nodes.Single()).SetValue("netserver");

        var result = await pipeline.ApplyAsync(pipeline.Prepare(snapshot));

        Assert.Equal(NutConfigurationApplyStatus.Success, result.Status);
        Assert.Equal("MODE=netserver\n", fileSystem.GetText(targetPath));
        Assert.NotNull(result.BackupPath);
        Assert.Equal("MODE=standalone\n", fileSystem.GetText(result.BackupPath!));
        Assert.True(fileSystem.ReplaceCalls >= 2);
    }

    [Fact]
    public async Task OutcomeUnknownInvalidatesSmbWriteCapabilityWithoutRetry()
    {
        var fileSystem = new FakeSmbFileSystem();
        var session = CreateSession(fileSystem);
        await session.ValidateConfigurationDirectoryAsync(ConfigurationDirectory);
        Assert.True((await session.ProbeSafeWriteCapabilityAsync(ConfigurationDirectory)).IsSupported);

        session.InvalidateSafeWriteCapability();

        Assert.False(session.IsSafeWriteCapabilityValidFor(ConfigurationDirectory));
        Assert.False((await session.ProbeSafeWriteCapabilityAsync(ConfigurationDirectory)).IsSupported);
    }

    [Fact]
    public async Task SmbCommitRejectsExternalTargetChangeBeforeReplace()
    {
        var fileSystem = new FakeSmbFileSystem();
        var targetPath = SmbUncPath.CombineDirectChild(ConfigurationDirectory, "nut.conf");
        var original = Encoding.UTF8.GetBytes("MODE=standalone\n");
        var candidate = Encoding.UTF8.GetBytes("MODE=netserver\n");
        fileSystem.SetFile(targetPath, Encoding.UTF8.GetString(original));
        var session = CreateSession(fileSystem);
        await session.ValidateConfigurationDirectoryAsync(ConfigurationDirectory);
        Assert.True((await session.ProbeSafeWriteCapabilityAsync(ConfigurationDirectory)).IsSupported);
        await session.UploadCandidateAsync(new RemoteNutCandidateUploadRequest(ConfigurationDirectory, "nut.conf", ".nutmanager-nut.conf-candidate.tmp", candidate));
        fileSystem.SetFile(targetPath, "MODE=external\n");

        var result = await session.CommitWindowsConfigurationAsync(new RemoteNutWindowsCommitRequest(
            ConfigurationDirectory,
            "nut.conf",
            ".nutmanager-nut.conf-candidate.tmp",
            ".nutmanager-nut.conf-original.bak",
            Fingerprint(original),
            Fingerprint(candidate)));

        Assert.Equal(RemoteNutTransportStatus.Failed, result.Status);
        Assert.Equal("MODE=external\n", fileSystem.GetText(targetPath));
    }

    [Fact]
    public async Task SmbRollbackRestoresOriginalAndPreservesReplacedContentInRecoveryBackup()
    {
        var fileSystem = new FakeSmbFileSystem();
        var targetPath = SmbUncPath.CombineDirectChild(ConfigurationDirectory, "nut.conf");
        var backupName = ".nutmanager-nut.conf-original.bak";
        var original = Encoding.UTF8.GetBytes("MODE=standalone\n");
        fileSystem.SetFile(targetPath, "MODE=netserver\n");
        fileSystem.SetFile(SmbUncPath.CombineDirectChild(ConfigurationDirectory, backupName), Encoding.UTF8.GetString(original));
        var session = CreateSession(fileSystem);
        await session.ValidateConfigurationDirectoryAsync(ConfigurationDirectory);
        Assert.True((await session.ProbeSafeWriteCapabilityAsync(ConfigurationDirectory)).IsSupported);

        var result = await session.RollbackWindowsConfigurationAsync(new RemoteNutWindowsRollbackRequest(
            ConfigurationDirectory,
            "nut.conf",
            backupName,
            ".nutmanager-nut.conf-rollback.tmp",
            ".nutmanager-nut.conf-recovery.bak",
            Fingerprint(original)));

        Assert.Equal(RemoteNutTransportStatus.Success, result.Status);
        Assert.Equal("MODE=standalone\n", fileSystem.GetText(targetPath));
        Assert.Equal("MODE=netserver\n", fileSystem.GetText(result.RecoveryPath!));
    }

    [Fact]
    public async Task CancelledSmbProbeDoesNotCreateWriteCapability()
    {
        var session = CreateSession(new FakeSmbFileSystem());
        await session.ValidateConfigurationDirectoryAsync(ConfigurationDirectory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ProbeSafeWriteCapabilityAsync(ConfigurationDirectory, cancellation.Token));

        Assert.False(session.IsSafeWriteCapabilityValidFor(ConfigurationDirectory));
    }

    private static WindowsSmbRemoteNutConfigurationSession CreateSession(FakeSmbFileSystem fileSystem) =>
        new(Share, true, fileSystem, new FakeNetworkConnection(), false);

    private static string Fingerprint(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class FakeNetworkConnection : IWindowsNetworkConnection
    {
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public string? LastShare { get; private set; }
        public string? LastUsername { get; private set; }
        public bool LastDisconnectForce { get; private set; }
        public WindowsNetworkConnectionResult ConnectResult { get; set; } = new(WindowsNetworkConnectionResult.Success);

        public Task<WindowsNetworkConnectionResult> ConnectAsync(string sharePath, string username, ReadOnlyMemory<char> password, CancellationToken cancellationToken)
        {
            ConnectCalls++;
            LastShare = sharePath;
            LastUsername = username;
            return Task.FromResult(ConnectResult);
        }

        public Task<WindowsNetworkConnectionResult> DisconnectAsync(string sharePath, CancellationToken cancellationToken)
        {
            DisconnectCalls++;
            LastShare = sharePath;
            LastDisconnectForce = false;
            return Task.FromResult(new WindowsNetworkConnectionResult(WindowsNetworkConnectionResult.Success));
        }
    }

    private sealed class FakeSmbFileSystem : ISmbFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public bool ReplaceThrows { get; set; }
        public bool FailCapabilityCleanup { get; set; }
        public int ReplaceCalls { get; private set; }
        public IReadOnlyCollection<string> FilePaths => _files.Keys;

        public void SetFile(string path, string value) => _files[path] = Encoding.UTF8.GetBytes(value);
        public string GetText(string path) => Encoding.UTF8.GetString(_files[path]);

        public Task<IReadOnlyList<SmbFileSystemEntry>> ListDirectoryAsync(string directory, CancellationToken cancellationToken)
        {
            var prefix = directory.TrimEnd('\\') + "\\";
            var entries = _files.Keys.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path[prefix.Length..].IndexOf('\\') < 0)
                .Select(path => new SmbFileSystemEntry(path[prefix.Length..], path, false, false))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult<IReadOnlyList<SmbFileSystemEntry>>(entries);
        }

        public Task<ReadOnlyMemory<byte>> ReadFileAsync(string path, CancellationToken cancellationToken)
        {
            if (!_files.TryGetValue(path, out var bytes))
            {
                throw new FileNotFoundException();
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(bytes.ToArray());
        }

        public Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            if (!_files.TryAdd(path, bytes.ToArray()))
            {
                throw new IOException("CreateNew collision");
            }

            return Task.CompletedTask;
        }

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken) => Task.FromResult(_files.ContainsKey(path));

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
        {
            if (FailCapabilityCleanup && path.Contains("capability", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("cleanup failure");
            }

            _files.Remove(path);
            return Task.CompletedTask;
        }

        public Task ReplaceFileAsync(string candidatePath, string targetPath, string backupPath, CancellationToken cancellationToken)
        {
            ReplaceCalls++;
            if (ReplaceThrows)
            {
                throw new IOException("unsupported");
            }

            _files[backupPath] = _files[targetPath].ToArray();
            _files[targetPath] = _files[candidatePath].ToArray();
            _files.Remove(candidatePath);
            return Task.CompletedTask;
        }

        public Task<bool> IsReparsePointAsync(string path, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
