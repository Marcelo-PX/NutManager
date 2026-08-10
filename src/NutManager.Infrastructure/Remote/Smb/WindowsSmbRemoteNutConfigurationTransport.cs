using System.Security.Cryptography;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Remote.Smb;

/// <summary>
/// Windows SMB configuration transport. SMB is strictly a file transport: it does not
/// create SSH/SFTP clients, execute PowerShell, discover shares, or administer services.
/// </summary>
public sealed class WindowsSmbRemoteNutConfigurationTransport : IRemoteNutConfigurationTransport
{
    private readonly ISmbFileSystem _fileSystem;
    private readonly IWindowsNetworkConnection _networkConnection;
    private readonly Func<bool> _isWindows;

    public WindowsSmbRemoteNutConfigurationTransport(
        ISmbFileSystem? fileSystem = null,
        IWindowsNetworkConnection? networkConnection = null,
        Func<bool>? isWindows = null)
    {
        _fileSystem = fileSystem ?? new WindowsSmbFileSystem();
        _networkConnection = networkConnection ?? new WindowsNetworkConnection();
        _isWindows = isWindows ?? OperatingSystem.IsWindows;
    }

    public async Task<RemoteNutConnectionResult> ConnectAsync(RemoteNutConfigurationConnectionRequest request, CancellationToken cancellationToken = default)
    {
        if (request is not SmbRemoteNutConnectionRequest smbRequest)
        {
            return new RemoteNutConnectionResult(RemoteNutConnectionState.Failed, message: "The SMB transport accepts only SMB configuration requests.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_isWindows())
        {
            return new RemoteNutConnectionResult(RemoteNutConnectionState.Failed, message: "O transporte SMB de configuração está disponível somente no Windows.");
        }

        var connectionOwned = false;
        if (smbRequest.AuthenticationMode == SmbAuthenticationMode.ExplicitCredentials)
        {
            var result = await _networkConnection.ConnectAsync(smbRequest.SharePath, smbRequest.Username!, smbRequest.Password, cancellationToken);
            if (!result.IsSuccess)
            {
                return result.ErrorCode == WindowsNetworkConnectionResult.CredentialConflict
                    ? new RemoteNutConnectionResult(RemoteNutConnectionState.AuthenticationFailed, message: "Já existe uma conexão Windows com este servidor usando outras credenciais. O NutManager não desconectará essa conexão automaticamente.")
                    : new RemoteNutConnectionResult(RemoteNutConnectionState.AuthenticationFailed, message: "Não foi possível estabelecer a sessão SMB com as credenciais informadas.");
            }

            connectionOwned = true;
        }

        return new RemoteNutConnectionResult(
            RemoteNutConnectionState.Connected,
            new WindowsSmbRemoteNutConfigurationSession(
                smbRequest.SharePath,
                smbRequest.CanWrite,
                _fileSystem,
                _networkConnection,
                connectionOwned));
    }
}

public sealed class WindowsSmbRemoteNutConfigurationSession : IRemoteNutConfigurationSession
{
    private readonly string _shareRoot;
    private readonly bool _canWrite;
    private readonly ISmbFileSystem _fileSystem;
    private readonly IWindowsNetworkConnection _networkConnection;
    private readonly bool _connectionOwned;
    private readonly HashSet<string> _validatedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly RemoteSafeWriteCapabilityState _safeWriteCapability = new();
    private bool _disposed;

    public WindowsSmbRemoteNutConfigurationSession(
        string shareRoot,
        bool canWrite,
        ISmbFileSystem fileSystem,
        IWindowsNetworkConnection networkConnection,
        bool connectionOwned)
    {
        _shareRoot = SmbUncPath.NormalizeShareRoot(shareRoot);
        _canWrite = canWrite;
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _networkConnection = networkConnection ?? throw new ArgumentNullException(nameof(networkConnection));
        _connectionOwned = connectionOwned;
    }

    public RemoteNutPlatform Platform => RemoteNutPlatform.Unknown;

    public string HomeDirectory => _shareRoot;

    public bool IsSafeWriteCapabilityValidFor(string configurationDirectory) =>
        _canWrite &&
        TryGetValidatedDirectory(configurationDirectory, out var normalizedDirectory) &&
        _safeWriteCapability.IsValidFor(normalizedDirectory);

    public async Task<RemoteNutDirectoryListing> BrowseDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalizedDirectory = NormalizeDirectory(directory);
        try
        {
            var entries = await _fileSystem.ListDirectoryAsync(normalizedDirectory, cancellationToken);
            return new RemoteNutDirectoryListing(
                normalizedDirectory,
                SmbUncPath.GetParentWithinShare(_shareRoot, normalizedDirectory),
                entries.Select(entry => new RemoteNutDirectoryEntry(entry.Name, entry.FullPath, entry.IsDirectory, entry.IsReparsePoint)).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return new RemoteNutDirectoryListing(normalizedDirectory, SmbUncPath.GetParentWithinShare(_shareRoot, normalizedDirectory), []);
        }
    }

    public async Task<RemoteNutDirectoryValidationResult> ValidateConfigurationDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalizedDirectory;
        try
        {
            normalizedDirectory = NormalizeDirectory(directory);
        }
        catch (ArgumentException)
        {
            return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.InvalidPath, directory, message: "O diretório SMB selecionado está fora do compartilhamento configurado.");
        }

        try
        {
            if (await _fileSystem.IsReparsePointAsync(normalizedDirectory, cancellationToken))
            {
                return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.Unsupported, normalizedDirectory, message: "O diretório SMB selecionado é um reparse point e não pode ser usado para escrita.");
            }

            var entries = await _fileSystem.ListDirectoryAsync(normalizedDirectory, cancellationToken);
            var names = entries.Where(entry => !entry.IsDirectory && RemoteNutConfigurationFiles.IsRecognized(entry.Name))
                .Select(entry => entry.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _validatedDirectories.Add(normalizedDirectory);
            return new RemoteNutDirectoryValidationResult(
                RemoteNutTransportStatus.Success,
                normalizedDirectory,
                names,
                names.Length == 0 ? "Nenhum arquivo de configuração NUT reconhecido foi encontrado no diretório selecionado." : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.Cancelled, normalizedDirectory, message: "A validação SMB foi cancelada.");
        }
        catch (UnauthorizedAccessException)
        {
            return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.AccessDenied, normalizedDirectory, message: "O diretório SMB selecionado não pode ser acessado.");
        }
        catch (DirectoryNotFoundException)
        {
            return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.NotFound, normalizedDirectory, message: "O diretório SMB selecionado não foi encontrado.");
        }
        catch (IOException)
        {
            return new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.Failed, normalizedDirectory, message: "Não foi possível validar o diretório SMB selecionado.");
        }
    }

    public async Task<RemoteNutFileReadResult> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!TryGetValidatedTarget(path, out var normalizedPath))
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.InvalidPath, message: "O arquivo SMB não pertence a um diretório de configuração validado.");
        }

        try
        {
            if (await _fileSystem.IsReparsePointAsync(normalizedPath, cancellationToken))
            {
                return new RemoteNutFileReadResult(RemoteNutTransportStatus.Unsupported, message: "O arquivo SMB é um reparse point e não pode ser acessado pela configuração remota.");
            }

            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Success, await _fileSystem.ReadFileAsync(normalizedPath, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Cancelled, message: "A leitura SMB foi cancelada.");
        }
        catch (FileNotFoundException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.NotFound, message: "O arquivo SMB não foi encontrado.");
        }
        catch (UnauthorizedAccessException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.AccessDenied, message: "O arquivo SMB não pode ser acessado.");
        }
        catch (IOException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Failed, message: "O arquivo SMB não pôde ser lido.");
        }
    }

    public async Task<RemoteNutWriteCapabilityResult> ProbeSafeWriteCapabilityAsync(string directory, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (!_canWrite)
        {
            return new RemoteNutWriteCapabilityResult(false, Platform, message: "O perfil SMB está configurado como somente leitura.");
        }

        if (!_safeWriteCapability.TryBeginProbe())
        {
            return new RemoteNutWriteCapabilityResult(false, Platform, message: "O resultado de uma escrita SMB anterior é indeterminado. Desconecte e conecte novamente antes de tentar gravar.");
        }

        if (!TryGetValidatedDirectory(directory, out var normalizedDirectory))
        {
            return new RemoteNutWriteCapabilityResult(false, Platform, message: "O diretório SMB precisa ser validado nesta sessão antes do teste de escrita.");
        }

        var token = Guid.NewGuid().ToString("N");
        var sourceName = $".nutmanager-smb-capability-{token}-source.tmp";
        var candidateName = $".nutmanager-smb-capability-{token}-candidate.tmp";
        var backupName = $".nutmanager-smb-capability-{token}-backup.bak";
        var sourcePath = SmbUncPath.CombineDirectChild(normalizedDirectory, sourceName);
        var candidatePath = SmbUncPath.CombineDirectChild(normalizedDirectory, candidateName);
        var backupPath = SmbUncPath.CombineDirectChild(normalizedDirectory, backupName);
        string? cleanupPath = null;
        RemoteNutWriteCapabilityResult result;
        try
        {
            var original = new byte[] { 0x31 };
            var candidate = new byte[] { 0x32 };
            await _fileSystem.WriteNewFileAsync(sourcePath, original, cancellationToken);
            await _fileSystem.WriteNewFileAsync(candidatePath, candidate, cancellationToken);
            if (!(await _fileSystem.ReadFileAsync(sourcePath, cancellationToken)).Span.SequenceEqual(original) ||
                !(await _fileSystem.ReadFileAsync(candidatePath, cancellationToken)).Span.SequenceEqual(candidate))
            {
                result = new RemoteNutWriteCapabilityResult(false, Platform, message: "A verificação dos arquivos temporários SMB falhou.");
            }
            else
            {
                await _fileSystem.ReplaceFileAsync(candidatePath, sourcePath, backupPath, cancellationToken);
                result = !(await _fileSystem.ReadFileAsync(sourcePath, cancellationToken)).Span.SequenceEqual(candidate) ||
                    !(await _fileSystem.ReadFileAsync(backupPath, cancellationToken)).Span.SequenceEqual(original)
                    ? new RemoteNutWriteCapabilityResult(false, Platform, message: "O compartilhamento SMB não confirmou a semântica necessária de File.Replace.")
                    : new RemoteNutWriteCapabilityResult(true, Platform);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = new RemoteNutWriteCapabilityResult(false, Platform, message: "O compartilhamento SMB não suporta a substituição segura exigida.");
        }
        finally
        {
            foreach (var path in new[] { sourcePath, candidatePath, backupPath })
            {
                if (!await TryDeleteCapabilityProbeFileAsync(path))
                {
                    cleanupPath ??= path;
                }
            }
        }

        if (cleanupPath is not null)
        {
            return new RemoteNutWriteCapabilityResult(false, Platform, cleanupPath, "A limpeza do teste SMB não pôde ser confirmada. Revise o arquivo temporário antes de tentar novamente.");
        }

        if (!result.IsSupported)
        {
            return result;
        }

        return _safeWriteCapability.TryCompleteProbe(normalizedDirectory)
            ? result
            : new RemoteNutWriteCapabilityResult(false, Platform, message: "O estado de escrita SMB foi invalidado durante a verificação.");
    }

    public void InvalidateSafeWriteCapability() => _safeWriteCapability.InvalidateSession();

    public async Task<RemoteNutFileReadResult> UploadCandidateAsync(RemoteNutCandidateUploadRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!RemoteNutConfigurationFiles.IsRecognized(request.TargetFileName) || !RemoteNutGeneratedTemporaryFile.IsValidName(request.TemporaryFileName) ||
            !IsSafeWriteCapabilityValidFor(request.ConfigurationDirectory))
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Unsupported, message: "A escrita SMB exige uma capacidade de substituição segura verificada para este diretório.");
        }

        var candidatePath = SmbUncPath.CombineDirectChild(NormalizeDirectory(request.ConfigurationDirectory), request.TemporaryFileName);
        try
        {
            await _fileSystem.WriteNewFileAsync(candidatePath, request.CandidateBytes, cancellationToken);
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Success, await _fileSystem.ReadFileAsync(candidatePath, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Cancelled, message: "O upload do candidato SMB foi cancelado.");
        }
        catch (UnauthorizedAccessException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.AccessDenied, message: "O candidato SMB não pode ser criado.");
        }
        catch (IOException)
        {
            return new RemoteNutFileReadResult(RemoteNutTransportStatus.Failed, message: "O candidato SMB não pode ser criado.");
        }
    }

    public async Task<RemoteNutTemporaryCleanupResult> DeleteGeneratedTemporaryFileAsync(string configurationDirectory, string temporaryFileName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!RemoteNutGeneratedTemporaryFile.IsValidName(temporaryFileName) || !TryGetValidatedDirectory(configurationDirectory, out var normalizedDirectory))
        {
            return new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.InvalidPath, "O caminho temporário SMB é inválido.");
        }

        var path = SmbUncPath.CombineDirectChild(normalizedDirectory, temporaryFileName);
        try
        {
            if (!await _fileSystem.FileExistsAsync(path, cancellationToken))
            {
                return new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.NotFound);
            }

            await _fileSystem.DeleteFileAsync(path, cancellationToken);
            return new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.Cancelled, "A limpeza do candidato SMB foi cancelada.");
        }
        catch (UnauthorizedAccessException)
        {
            return new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.AccessDenied, "O candidato SMB não pode ser removido.");
        }
        catch (IOException)
        {
            return new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.Failed, "A limpeza do candidato SMB falhou.");
        }
    }

    public async Task<RemoteNutCommitResult> CommitWindowsConfigurationAsync(RemoteNutWindowsCommitRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsSafeCommitRequest(request) || !IsSafeWriteCapabilityValidFor(request.ConfigurationDirectory))
        {
            return new RemoteNutCommitResult(RemoteNutTransportStatus.Unsupported, message: "A substituição SMB segura não está disponível para este diretório.");
        }

        var directory = NormalizeDirectory(request.ConfigurationDirectory);
        var targetPath = SmbUncPath.CombineDirectChild(directory, request.TargetFileName);
        var candidatePath = SmbUncPath.CombineDirectChild(directory, request.TemporaryFileName);
        var backupPath = SmbUncPath.CombineDirectChild(directory, request.BackupFileName);
        try
        {
            if (await IsWriteReparsePointAsync(targetPath, candidatePath, cancellationToken))
            {
                return new RemoteNutCommitResult(RemoteNutTransportStatus.Unsupported, message: "O caminho SMB de escrita é um reparse point.");
            }

            var target = await _fileSystem.ReadFileAsync(targetPath, cancellationToken);
            var candidate = await _fileSystem.ReadFileAsync(candidatePath, cancellationToken);
            if (!FingerprintMatches(target.Span, request.ExpectedOriginalFingerprint) || !FingerprintMatches(candidate.Span, request.ExpectedCandidateFingerprint))
            {
                return new RemoteNutCommitResult(RemoteNutTransportStatus.Failed, message: "A configuração SMB foi alterada externamente antes da substituição.");
            }

            await _fileSystem.ReplaceFileAsync(candidatePath, targetPath, backupPath, cancellationToken);
            return new RemoteNutCommitResult(RemoteNutTransportStatus.Success, backupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var knownNotExecuted = await IsProvenUnreplacedAsync(targetPath, candidatePath, request.ExpectedOriginalFingerprint, request.ExpectedCandidateFingerprint);
            if (knownNotExecuted)
            {
                return new RemoteNutCommitResult(RemoteNutTransportStatus.Failed, message: "A substituição SMB foi rejeitada antes de ser concluída.");
            }

            InvalidateSafeWriteCapability();
            return new RemoteNutCommitResult(RemoteNutTransportStatus.OutcomeUnknown, message: "O resultado da substituição SMB não pôde ser confirmado.");
        }
    }

    public async Task<RemoteNutCommitResult> RollbackWindowsConfigurationAsync(RemoteNutWindowsRollbackRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!RemoteNutConfigurationFiles.IsRecognized(request.TargetFileName) || !RemoteNutGeneratedBackupFile.IsValidName(request.BackupFileName) ||
            !RemoteNutGeneratedTemporaryFile.IsValidName(request.RollbackFileName) || !RemoteNutGeneratedBackupFile.IsValidName(request.RecoveryFileName) ||
            !IsSafeWriteCapabilityValidFor(request.ConfigurationDirectory))
        {
            return new RemoteNutCommitResult(RemoteNutTransportStatus.Unsupported, message: "O rollback SMB seguro não está disponível para este diretório.");
        }

        var directory = NormalizeDirectory(request.ConfigurationDirectory);
        var targetPath = SmbUncPath.CombineDirectChild(directory, request.TargetFileName);
        var backupPath = SmbUncPath.CombineDirectChild(directory, request.BackupFileName);
        var rollbackPath = SmbUncPath.CombineDirectChild(directory, request.RollbackFileName);
        var recoveryPath = SmbUncPath.CombineDirectChild(directory, request.RecoveryFileName);
        try
        {
            if (await IsWriteReparsePointAsync(targetPath, backupPath, cancellationToken))
            {
                return new RemoteNutCommitResult(RemoteNutTransportStatus.Unsupported, message: "O caminho SMB de rollback é um reparse point.");
            }

            var original = await _fileSystem.ReadFileAsync(backupPath, cancellationToken);
            if (!FingerprintMatches(original.Span, request.ExpectedOriginalFingerprint))
            {
                return new RemoteNutCommitResult(RemoteNutTransportStatus.Failed, message: "O backup SMB não corresponde à configuração original.");
            }

            await _fileSystem.WriteNewFileAsync(rollbackPath, original, cancellationToken);
            await _fileSystem.ReplaceFileAsync(rollbackPath, targetPath, recoveryPath, cancellationToken);
            return new RemoteNutCommitResult(RemoteNutTransportStatus.Success, recoveryPath: recoveryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            InvalidateSafeWriteCapability();
            return new RemoteNutCommitResult(RemoteNutTransportStatus.OutcomeUnknown, message: "O resultado do rollback SMB não pôde ser confirmado.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_connectionOwned)
        {
            await _networkConnection.DisconnectAsync(_shareRoot, CancellationToken.None);
        }
    }

    private string NormalizeDirectory(string directory) =>
        SmbUncPath.NormalizeConfigurationDirectory(_shareRoot, directory)
        ?? throw new ArgumentException("An SMB configuration directory is required.", nameof(directory));

    private bool TryGetValidatedDirectory(string directory, out string normalizedDirectory)
    {
        try
        {
            normalizedDirectory = NormalizeDirectory(directory);
            return _validatedDirectories.Contains(normalizedDirectory);
        }
        catch (ArgumentException)
        {
            normalizedDirectory = string.Empty;
            return false;
        }
    }

    private bool TryGetValidatedTarget(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        try
        {
            var separator = path.LastIndexOf('\\');
            if (separator <= 0)
            {
                return false;
            }

            var directory = NormalizeDirectory(path[..separator]);
            var fileName = path[(separator + 1)..];
            if (!IsAllowedConfigurationChildName(fileName) || !_validatedDirectories.Contains(directory))
            {
                return false;
            }

            normalizedPath = SmbUncPath.CombineDirectChild(directory, fileName);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task<bool> IsWriteReparsePointAsync(string targetPath, string candidatePath, CancellationToken cancellationToken) =>
        await _fileSystem.IsReparsePointAsync(targetPath, cancellationToken) || await _fileSystem.IsReparsePointAsync(candidatePath, cancellationToken);

    private async Task<bool> TryDeleteCapabilityProbeFileAsync(string path)
    {
        try
        {
            if (await _fileSystem.FileExistsAsync(path, CancellationToken.None))
            {
                await _fileSystem.DeleteFileAsync(path, CancellationToken.None);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> IsProvenUnreplacedAsync(string targetPath, string candidatePath, string originalFingerprint, string candidateFingerprint)
    {
        try
        {
            var target = await _fileSystem.ReadFileAsync(targetPath, CancellationToken.None);
            var candidate = await _fileSystem.ReadFileAsync(candidatePath, CancellationToken.None);
            return FingerprintMatches(target.Span, originalFingerprint) && FingerprintMatches(candidate.Span, candidateFingerprint);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeCommitRequest(RemoteNutWindowsCommitRequest request) =>
        RemoteNutConfigurationFiles.IsRecognized(request.TargetFileName) &&
        RemoteNutGeneratedTemporaryFile.IsValidName(request.TemporaryFileName) &&
        RemoteNutGeneratedBackupFile.IsValidName(request.BackupFileName);

    private static bool IsAllowedConfigurationChildName(string fileName) =>
        RemoteNutConfigurationFiles.IsRecognized(fileName) ||
        RemoteNutGeneratedTemporaryFile.IsValidName(fileName) ||
        RemoteNutGeneratedBackupFile.IsValidName(fileName);

    private static bool FingerprintMatches(ReadOnlySpan<byte> bytes, string expectedFingerprint) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedFingerprint, StringComparison.OrdinalIgnoreCase);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsSmbRemoteNutConfigurationSession));
        }
    }
}
