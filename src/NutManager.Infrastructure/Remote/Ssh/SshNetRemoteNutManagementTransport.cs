using Renci.SshNet;
using Renci.SshNet.Common;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Remote.Ssh;

public sealed class SshNetRemoteNutManagementTransport : IRemoteNutManagementTransport
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public async Task<RemoteNutConnectionResult> ConnectAsync(RemoteNutConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        RemoteNutHostKeyInfo? receivedHostKey = null;
        SshClient? sshClient = null;
        SftpClient? sftpClient = null;
        try
        {
            sshClient = CreateSshClient(request, hostKey => receivedHostKey = hostKey);
            await ConnectBoundedAsync(sshClient, cancellationToken);
            sftpClient = CreateSftpClient(request, hostKey => receivedHostKey = hostKey);
            await ConnectBoundedAsync(sftpClient, cancellationToken);
            return new RemoteNutConnectionResult(
                RemoteNutConnectionState.Connected,
                new SshNetRemoteNutManagementSession(sshClient, sftpClient),
                receivedHostKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sshClient?.Dispose();
            sftpClient?.Dispose();
            return new RemoteNutConnectionResult(RemoteNutConnectionState.Disconnected, message: "Remote connection was cancelled.");
        }
        catch (TimeoutException)
        {
            sshClient?.Dispose();
            sftpClient?.Dispose();
            return new RemoteNutConnectionResult(RemoteNutConnectionState.Timeout, hostKey: receivedHostKey, message: "Remote connection timed out.");
        }
        catch (SshAuthenticationException)
        {
            sshClient?.Dispose();
            sftpClient?.Dispose();
            return new RemoteNutConnectionResult(RemoteNutConnectionState.AuthenticationFailed, hostKey: receivedHostKey, message: "SSH authentication failed.");
        }
        catch (SshConnectionException)
        {
            sshClient?.Dispose();
            sftpClient?.Dispose();
            var state = receivedHostKey is null
                ? RemoteNutConnectionState.ConnectionFailed
                : string.IsNullOrWhiteSpace(request.TrustedHostKeyFingerprint)
                    ? RemoteNutConnectionState.HostKeyTrustRequired
                    : RemoteNutConnectionState.HostKeyMismatch;
            return new RemoteNutConnectionResult(state, hostKey: receivedHostKey, message: "SSH host key verification failed.");
        }
        catch (Exception)
        {
            sshClient?.Dispose();
            sftpClient?.Dispose();
            return new RemoteNutConnectionResult(RemoteNutConnectionState.ConnectionFailed, hostKey: receivedHostKey, message: "Remote connection could not be established.");
        }
    }

    private static SshClient CreateSshClient(RemoteNutConnectionRequest request, Action<RemoteNutHostKeyInfo> hostKeyReceived)
    {
        var client = new SshClient(CreateConnectionInfo(request));
        client.HostKeyReceived += (_, eventArgs) => ValidateHostKey(request, eventArgs, hostKeyReceived);
        return client;
    }

    private static SftpClient CreateSftpClient(RemoteNutConnectionRequest request, Action<RemoteNutHostKeyInfo> hostKeyReceived)
    {
        var client = new SftpClient(CreateConnectionInfo(request));
        client.HostKeyReceived += (_, eventArgs) => ValidateHostKey(request, eventArgs, hostKeyReceived);
        return client;
    }

    private static ConnectionInfo CreateConnectionInfo(RemoteNutConnectionRequest request)
    {
        AuthenticationMethod authentication = request.Authentication switch
        {
            RemoteNutPasswordAuthentication password => new PasswordAuthenticationMethod(request.Username, new string(password.Password.Span)),
            RemoteNutPrivateKeyAuthentication key when key.Passphrase.IsEmpty => new PrivateKeyAuthenticationMethod(request.Username, new PrivateKeyFile(key.PrivateKeyPath)),
            RemoteNutPrivateKeyAuthentication key => new PrivateKeyAuthenticationMethod(request.Username, new PrivateKeyFile(key.PrivateKeyPath, new string(key.Passphrase.Span))),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported remote authentication type.")
        };
        return new ConnectionInfo(request.Host, request.Port, request.Username, authentication);
    }

    private static void ValidateHostKey(RemoteNutConnectionRequest request, HostKeyEventArgs eventArgs, Action<RemoteNutHostKeyInfo> hostKeyReceived)
    {
        var fingerprint = SshHostKeyFingerprint.Create(eventArgs.HostKey);
        var hostKey = new RemoteNutHostKeyInfo(request.Host, request.Port, eventArgs.HostKeyName, fingerprint);
        hostKeyReceived(hostKey);
        eventArgs.CanTrust = SshHostKeyFingerprint.Matches(request.TrustedHostKeyFingerprint, eventArgs.HostKey);
    }

    private static async Task ConnectBoundedAsync(BaseClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connect = Task.Run(client.Connect);
        try
        {
            await connect.WaitAsync(ConnectTimeout, CancellationToken.None);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}

public sealed class SshNetRemoteNutManagementSession : IRemoteNutManagementSession
{
    private static readonly TimeSpan SftpTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly SshClient _sshClient;
    private readonly SftpClient _sftpClient;
    private bool _disposed;

    public SshNetRemoteNutManagementSession(SshClient sshClient, SftpClient sftpClient)
    {
        _sshClient = sshClient ?? throw new ArgumentNullException(nameof(sshClient));
        _sftpClient = sftpClient ?? throw new ArgumentNullException(nameof(sftpClient));
        HomeDirectory = _sftpClient.WorkingDirectory;
    }

    public RemoteNutPlatform Platform { get; private set; } = RemoteNutPlatform.Unknown;

    public string HomeDirectory { get; }

    public async Task<RemoteNutDirectoryListing> BrowseDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var sftpPath = RemotePathMapper.ToSftpPath(directory);
        return await ExecuteSftpAsync(() =>
        {
            var entries = _sftpClient.ListDirectory(sftpPath)
                .Where(entry => entry.Name is not "." and not "..")
                .Where(entry => entry.IsDirectory)
                .Select(entry => new RemoteNutDirectoryEntry(entry.Name, entry.FullName, true, entry.IsSymbolicLink))
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ToArray();
            return new RemoteNutDirectoryListing(sftpPath, GetParentPath(sftpPath), entries);
        }, cancellationToken);
    }

    public async Task<RemoteNutDirectoryValidationResult> ValidateConfigurationDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var sftpPath = RemotePathMapper.ToSftpPath(directory);
        try
        {
            var present = await ExecuteSftpAsync(() => _sftpClient.ListDirectory(sftpPath)
                .Where(entry => !entry.IsDirectory && RemoteNutConfigurationFiles.IsRecognized(entry.Name))
                .Select(entry => entry.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray(), cancellationToken);
            return new RemoteNutDirectoryValidationResult(
                RemoteNutTransportStatus.Success,
                sftpPath,
                present,
                present.Length == 0 ? "No recognized NUT configuration file was found in the selected directory." : null);
        }
        catch (SftpPermissionDeniedException)
        {
            return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.AccessDenied, sftpPath, message: "The selected remote directory cannot be accessed.");
        }
        catch (SftpPathNotFoundException)
        {
            return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.NotFound, sftpPath, message: "The selected remote directory was not found.");
        }
        catch (TimeoutException)
        {
            return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.Timeout, sftpPath, message: "Remote directory validation timed out.");
        }
        catch
        {
            return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.Failed, sftpPath, message: "The selected remote directory could not be validated.");
        }
    }

    public async Task<RemoteNutFileReadResult> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var sftpPath = RemotePathMapper.ToSftpPath(path);
        try
        {
            var bytes = await ExecuteSftpAsync(() =>
            {
                using var stream = new MemoryStream();
                _sftpClient.DownloadFile(sftpPath, stream);
                return stream.ToArray();
            }, cancellationToken);
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Success, bytes);
        }
        catch (SftpPathNotFoundException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.NotFound, message: "The remote file was not found.");
        }
        catch (SftpPermissionDeniedException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.AccessDenied, message: "The remote file cannot be accessed.");
        }
        catch (TimeoutException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Timeout, message: "The remote file operation timed out.");
        }
        catch
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Failed, message: "The remote file could not be read.");
        }
    }

    public async Task<RemoteNutWriteCapabilityResult> ProbeSafeWriteCapabilityAsync(string directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sftpDirectory = RemotePathMapper.ToSftpPath(directory);
        if (!await ProbeWindowsPlatformAsync())
        {
            return new RemoteNutWriteCapabilityResult(false, Platform, message: "Remote configuration writing is available only for Windows servers managed through OpenSSH.");
        }

        var token = Guid.NewGuid().ToString("N");
        var sourceName = $".nutmanager-capability-{token}-source.tmp";
        var candidateName = $".nutmanager-capability-{token}-candidate.tmp";
        var backupName = $".nutmanager-capability-{token}-backup.tmp";
        string? cleanupPath = null;
        RemoteNutWriteCapabilityResult result;
        try
        {
            await WriteNewAsync(RemotePathMapper.Combine(sftpDirectory, sourceName), new byte[] { 0x31 }, cancellationToken);
            await WriteNewAsync(RemotePathMapper.Combine(sftpDirectory, candidateName), new byte[] { 0x32 }, cancellationToken);
            var command = RemoteWindowsCommandBuilder.BuildWindowsCapabilityProbe(sftpDirectory, sourceName, candidateName, backupName);
            var output = await ExecuteCommitCommandAsync(command);
            if (!output.Contains("NUTMANAGER_PROBE_OK", StringComparison.Ordinal))
            {
                result = new RemoteNutWriteCapabilityResult(false, Platform, message: "The Windows replace capability probe failed.");
            }
            else
            {
                result = new RemoteNutWriteCapabilityResult(true, Platform);
            }
        }
        catch (Exception)
        {
            result = new RemoteNutWriteCapabilityResult(false, Platform, message: "The remote write capability probe failed.");
        }
        finally
        {
            foreach (var name in new[] { sourceName, candidateName, backupName })
            {
                var path = RemotePathMapper.Combine(sftpDirectory, name);
                if (!await DeleteIfExistsBoundedAsync(path))
                {
                    cleanupPath ??= path;
                }
            }
        }

        return cleanupPath is null
            ? result
            : new RemoteNutWriteCapabilityResult(
                false,
                Platform,
                cleanupPath,
                "The remote capability probe cleanup could not be confirmed. Review the remote temporary file before retrying.");
    }

    public async Task<RemoteNutFileReadResult> UploadCandidateAsync(RemoteNutCandidateUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RemoteNutConfigurationFiles.IsRecognized(request.TargetFileName) || !IsGeneratedTemporaryName(request.TemporaryFileName))
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.InvalidPath, message: "The remote candidate target is invalid.");
        }

        var path = RemotePathMapper.Combine(RemotePathMapper.ToSftpPath(request.ConfigurationDirectory), request.TemporaryFileName);
        try
        {
            await WriteNewAsync(path, request.CandidateBytes, cancellationToken);
            return await ReadFileAsync(path, cancellationToken);
        }
        catch (SftpPermissionDeniedException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.AccessDenied, message: "The remote candidate file cannot be created.");
        }
        catch (TimeoutException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Timeout, message: "The remote candidate upload timed out.");
        }
        catch
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Failed, message: "The remote candidate file could not be created.");
        }
    }

    public async Task<RemoteNutCommitResult> CommitWindowsConfigurationAsync(RemoteNutWindowsCommitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (Platform != RemoteNutPlatform.Windows || !IsCommitRequestSafe(request))
        {
            return new RemoteNutCommitResult(RemoteNutTransportStatus.Unsupported, message: "Remote Windows safe write is not available.");
        }

        try
        {
            var output = await ExecuteCommitCommandAsync(RemoteWindowsCommandBuilder.BuildWindowsCommit(request));
            return output.Contains("NUTMANAGER_COMMIT_OK", StringComparison.Ordinal)
                ? new RemoteNutCommitResult(RemoteNutTransportStatus.Success, RemotePathMapper.Combine(request.ConfigurationDirectory, request.BackupFileName))
                : new RemoteNutCommitResult(RemoteNutTransportStatus.Failed, message: "The remote configuration commit was rejected.");
        }
        catch (TimeoutException)
        {
            return new RemoteNutCommitResult(RemoteNutTransportStatus.OutcomeUnknown, message: "The remote configuration commit outcome could not be confirmed.");
        }
        catch
        {
            return new RemoteNutCommitResult(RemoteNutTransportStatus.OutcomeUnknown, message: "The remote configuration commit outcome could not be confirmed.");
        }
    }

    public async Task<RemoteNutCommitResult> RollbackWindowsConfigurationAsync(RemoteNutWindowsRollbackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Platform != RemoteNutPlatform.Windows || !RemoteNutConfigurationFiles.IsRecognized(request.TargetFileName) ||
            !IsGeneratedTemporaryName(request.RollbackFileName) || !IsGeneratedBackupName(request.RecoveryFileName))
        {
            return new RemoteNutCommitResult(RemoteNutTransportStatus.Unsupported, message: "Remote Windows rollback is not available.");
        }

        try
        {
            var output = await ExecuteCommitCommandAsync(RemoteWindowsCommandBuilder.BuildWindowsRollback(request));
            return output.Contains("NUTMANAGER_ROLLBACK_OK", StringComparison.Ordinal)
                ? new RemoteNutCommitResult(RemoteNutTransportStatus.Success, recoveryPath: RemotePathMapper.Combine(request.ConfigurationDirectory, request.RecoveryFileName))
                : new RemoteNutCommitResult(RemoteNutTransportStatus.Failed, message: "The remote rollback was rejected.");
        }
        catch
        {
            return new RemoteNutCommitResult(RemoteNutTransportStatus.OutcomeUnknown, message: "The remote rollback outcome could not be confirmed.");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _sftpClient.Dispose();
            _sshClient.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<bool> ProbeWindowsPlatformAsync()
    {
        if (Platform != RemoteNutPlatform.Unknown)
        {
            return Platform == RemoteNutPlatform.Windows;
        }

        try
        {
            var output = await ExecuteCommitCommandAsync(RemoteWindowsCommandBuilder.BuildWindowsPlatformProbe());
            Platform = output.Contains("NUTMANAGER_WINDOWS", StringComparison.Ordinal) ? RemoteNutPlatform.Windows : RemoteNutPlatform.NonWindows;
        }
        catch
        {
            Platform = RemoteNutPlatform.Unknown;
        }

        return Platform == RemoteNutPlatform.Windows;
    }

    private async Task<T> ExecuteSftpAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var task = Task.Run(operation);
        return await task.WaitAsync(SftpTimeout, CancellationToken.None);
    }

    private async Task WriteNewAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        await ExecuteSftpAsync(() =>
        {
            using var stream = _sftpClient.Open(path, FileMode.CreateNew, FileAccess.Write);
            stream.Write(bytes.Span);
            stream.Flush();
            return true;
        }, cancellationToken);
    }

    private async Task<string> ExecuteCommitCommandAsync(string command)
    {
        ThrowIfDisposed();
        var task = Task.Run(() => _sshClient.RunCommand(command));
        var result = await task.WaitAsync(CommitTimeout, CancellationToken.None);
        return result.Result.Length > 4096 ? result.Result[..4096] : result.Result;
    }

    private async Task<bool> DeleteIfExistsBoundedAsync(string path)
    {
        try
        {
            ThrowIfDisposed();
            var task = Task.Run(() =>
            {
                if (_sftpClient.Exists(path))
                {
                    _sftpClient.DeleteFile(path);
                }
            });
            await task.WaitAsync(CleanupTimeout, CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGeneratedTemporaryName(string name) =>
        name.StartsWith(".nutmanager-", StringComparison.Ordinal) &&
        name.EndsWith(".tmp", StringComparison.Ordinal) &&
        name.IndexOfAny(['/', '\\']) < 0;

    private static bool IsGeneratedBackupName(string name) =>
        name.StartsWith(".nutmanager-", StringComparison.Ordinal) &&
        name.EndsWith(".bak", StringComparison.Ordinal) &&
        name.IndexOfAny(['/', '\\']) < 0;

    private static bool IsCommitRequestSafe(RemoteNutWindowsCommitRequest request) =>
        RemoteNutConfigurationFiles.IsRecognized(request.TargetFileName) &&
        IsGeneratedTemporaryName(request.TemporaryFileName) &&
        IsGeneratedBackupName(request.BackupFileName);

    private static string? GetParentPath(string path)
    {
        var slash = path.TrimEnd('/').LastIndexOf('/');
        return slash < 0 ? null : slash == 0 ? "/" : path[..slash];
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SshNetRemoteNutManagementSession));
        }
    }
}
