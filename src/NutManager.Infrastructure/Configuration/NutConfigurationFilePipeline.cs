using System.Security.Cryptography;
using System.Text;
using NutManager.Core.Configuration;

namespace NutManager.Infrastructure.Configuration;

public sealed class NutConfigurationFilePipeline
{
    private const string RedactedText = "<redacted>";

    private readonly INutConfigurationFileSystem _fileSystem;
    private readonly NutConfigurationParser _parser;
    private readonly INutConfigurationCandidateValidator _candidateValidator;
    private readonly INutConfigurationPostApplyValidator _postApplyValidator;

    public NutConfigurationFilePipeline(
        INutConfigurationFileSystem? fileSystem = null,
        NutConfigurationParser? parser = null,
        INutConfigurationCandidateValidator? candidateValidator = null,
        INutConfigurationPostApplyValidator? postApplyValidator = null)
    {
        _fileSystem = fileSystem ?? new NutConfigurationFileSystem();
        _parser = parser ?? new NutConfigurationParser();
        _candidateValidator = candidateValidator ?? AcceptingCandidateValidator.Instance;
        _postApplyValidator = postApplyValidator ?? AcceptingPostApplyValidator.Instance;
    }

    public async Task<NutConfigurationLoadResult> LoadAsync(
        string targetPath,
        NutConfigurationFileKind fileKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        try
        {
            if (!await _fileSystem.FileExistsAsync(targetPath, cancellationToken))
            {
                return new NutConfigurationLoadResult(NutConfigurationLoadStatus.TargetNotFound, message: "Configuration file was not found.");
            }

            var originalBytes = await _fileSystem.ReadAllBytesAsync(targetPath, cancellationToken);
            var (encoding, originalText) = NutConfigurationTextCodec.Decode(originalBytes);
            var document = _parser.Parse(fileKind, originalText);
            var snapshot = new NutConfigurationFileSnapshot(
                targetPath,
                fileKind,
                document,
                encoding,
                Fingerprint(originalBytes),
                originalBytes.LongLength);
            return new NutConfigurationLoadResult(NutConfigurationLoadStatus.Success, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new NutConfigurationLoadResult(NutConfigurationLoadStatus.Cancelled, message: "Configuration load was cancelled.");
        }
        catch (DecoderFallbackException)
        {
            return new NutConfigurationLoadResult(NutConfigurationLoadStatus.UnsupportedEncoding, message: "Configuration encoding is not supported.");
        }
        catch (UnauthorizedAccessException)
        {
            return new NutConfigurationLoadResult(NutConfigurationLoadStatus.AccessDenied, message: "Configuration file cannot be accessed.");
        }
        catch (FileNotFoundException)
        {
            return new NutConfigurationLoadResult(NutConfigurationLoadStatus.TargetNotFound, message: "Configuration file was not found.");
        }
        catch (IOException)
        {
            return new NutConfigurationLoadResult(NutConfigurationLoadStatus.Failed, message: "Configuration file could not be read.");
        }
    }

    public NutConfigurationPreparedChange Prepare(NutConfigurationFileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var candidateText = snapshot.Document.Serialize();
        var candidateBytes = NutConfigurationTextCodec.Encode(candidateText, snapshot.Encoding);
        var candidateFingerprint = Fingerprint(candidateBytes);
        var preview = BuildPreview(snapshot, candidateText, candidateFingerprint);
        return new NutConfigurationPreparedChange(snapshot, candidateText, candidateBytes, candidateFingerprint, preview);
    }

    public async Task<NutConfigurationApplyResult> ApplyAsync(
        NutConfigurationPreparedChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!change.HasChanges)
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.NoChanges, message: "Configuration has no changes to apply.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Cancelled, message: "Configuration apply was cancelled.");
        }

        var targetPath = change.Snapshot.TargetPath;
        var tempPath = CreateTemporaryPath(targetPath);
        var backupPath = CreateBackupPath(targetPath);
        var replaced = false;

        try
        {
            if (!await _fileSystem.FileExistsAsync(targetPath, cancellationToken))
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.TargetNotFound, message: "Configuration file was not found.");
            }

            var currentBytes = await _fileSystem.ReadAllBytesAsync(targetPath, cancellationToken);
            if (!MatchesOriginal(change.Snapshot, currentBytes))
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.ChangedExternally, message: "Configuration changed externally.");
            }

            try
            {
                await _fileSystem.WriteNewFileAsync(tempPath, change.CandidateBytes, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Cancelled, message: "Configuration apply was cancelled.");
            }
            catch (IOException)
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.TempWriteFailed, message: "Candidate file could not be written.");
            }
            catch (UnauthorizedAccessException)
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.TempWriteFailed, message: "Candidate file could not be written.");
            }

            try
            {
                if (!await ValidateCandidateAsync(tempPath, change, cancellationToken))
                {
                    return new NutConfigurationApplyResult(NutConfigurationApplyStatus.CandidateValidationFailed, message: "Candidate configuration validation failed.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Cancelled, message: "Configuration apply was cancelled.");
            }
            catch (Exception)
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.CandidateValidationFailed, message: "Candidate configuration validation failed.");
            }

            currentBytes = await _fileSystem.ReadAllBytesAsync(targetPath, cancellationToken);
            if (!MatchesOriginal(change.Snapshot, currentBytes))
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.ChangedExternally, message: "Configuration changed externally.");
            }

            try
            {
                await _fileSystem.ReplaceAsync(tempPath, targetPath, backupPath, CancellationToken.None);
                replaced = true;
            }
            catch (IOException)
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.ReplaceFailed, message: "Configuration file could not be replaced.");
            }
            catch (UnauthorizedAccessException)
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.ReplaceFailed, message: "Configuration file could not be replaced.");
            }

            try
            {
                var replacedBackup = await _fileSystem.ReadAllBytesAsync(backupPath, CancellationToken.None);
                if (!MatchesOriginal(change.Snapshot, replacedBackup))
                {
                    var rollback = await RestoreFromBackupAsync(backupPath, targetPath, Fingerprint(replacedBackup));
                    return new NutConfigurationApplyResult(
                        rollback.Succeeded ? NutConfigurationApplyStatus.ChangedExternally : NutConfigurationApplyStatus.ChangedExternallyRollbackFailed,
                        backupPath,
                        "Configuration changed externally during replacement.",
                        rollback.Succeeded,
                        rollback.RecoveryPath);
                }

                var destinationBytes = await _fileSystem.ReadAllBytesAsync(targetPath, CancellationToken.None);
                if (!destinationBytes.AsSpan().SequenceEqual(change.CandidateBytes.Span))
                {
                    return await RollbackAsync(
                        NutConfigurationApplyStatus.VerificationFailedRolledBack,
                        NutConfigurationApplyStatus.VerificationFailedRollbackFailed,
                        backupPath,
                        targetPath,
                        change.Snapshot.OriginalFingerprint,
                        "Configuration verification after replacement failed.");
                }

                try
                {
                    var postApplyResult = await _postApplyValidator.ValidateAsync(change, CancellationToken.None);
                    if (postApplyResult.IsValid)
                    {
                        return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Success, backupPath);
                    }
                }
                catch (Exception)
                {
                    // A post-apply hook failure follows the same recoverable path as a validation failure.
                }

                return await RollbackAsync(
                    NutConfigurationApplyStatus.PostApplyValidationFailedRolledBack,
                    NutConfigurationApplyStatus.PostApplyValidationFailedRollbackFailed,
                    backupPath,
                    targetPath,
                    change.Snapshot.OriginalFingerprint,
                    "Post-apply configuration validation failed.");
            }
            catch (Exception)
            {
                return await RollbackAsync(
                    NutConfigurationApplyStatus.VerificationFailedRolledBack,
                    NutConfigurationApplyStatus.VerificationFailedRollbackFailed,
                    backupPath,
                    targetPath,
                    change.Snapshot.OriginalFingerprint,
                    "Configuration verification after replacement failed.");
            }
        }
        catch (OperationCanceledException) when (!replaced && cancellationToken.IsCancellationRequested)
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Cancelled, message: "Configuration apply was cancelled.");
        }
        catch (FileNotFoundException) when (!replaced)
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.TargetNotFound, message: "Configuration file was not found.");
        }
        catch (IOException) when (!replaced)
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Failed, message: "Configuration apply failed.");
        }
        catch (UnauthorizedAccessException) when (!replaced)
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Failed, message: "Configuration apply failed.");
        }
        finally
        {
            if (!replaced)
            {
                await DeleteBestEffortAsync(tempPath);
            }
        }
    }

    private async Task<bool> ValidateCandidateAsync(
        string tempPath,
        NutConfigurationPreparedChange change,
        CancellationToken cancellationToken)
    {
        var tempBytes = await _fileSystem.ReadAllBytesAsync(tempPath, cancellationToken);
        if (!tempBytes.AsSpan().SequenceEqual(change.CandidateBytes.Span))
        {
            return false;
        }

        var (encoding, candidateText) = NutConfigurationTextCodec.Decode(tempBytes);
        if (encoding != change.Snapshot.Encoding || !string.Equals(candidateText, change.CandidateText, StringComparison.Ordinal))
        {
            return false;
        }

        var candidateDocument = _parser.Parse(change.Snapshot.FileKind, candidateText);
        if (!string.Equals(candidateDocument.Serialize(), candidateText, StringComparison.Ordinal))
        {
            return false;
        }

        var reencodedBytes = NutConfigurationTextCodec.Encode(candidateDocument.Serialize(), encoding);
        if (!reencodedBytes.AsSpan().SequenceEqual(change.CandidateBytes.Span))
        {
            return false;
        }

        var result = await _candidateValidator.ValidateAsync(change, cancellationToken);
        return result.IsValid;
    }

    private async Task<NutConfigurationApplyResult> RollbackAsync(
        NutConfigurationApplyStatus rollbackSucceededStatus,
        NutConfigurationApplyStatus rollbackFailedStatus,
        string backupPath,
        string targetPath,
        string expectedFingerprint,
        string message)
    {
        var rollback = await RestoreFromBackupAsync(backupPath, targetPath, expectedFingerprint);
        return new NutConfigurationApplyResult(
            rollback.Succeeded ? rollbackSucceededStatus : rollbackFailedStatus,
            backupPath,
            message,
            rollback.Succeeded,
            rollback.RecoveryPath);
    }

    private async Task<RollbackResult> RestoreFromBackupAsync(string backupPath, string targetPath, string expectedFingerprint)
    {
        var rollbackTempPath = CreateTemporaryPath(targetPath);
        var recoveryPath = CreateRecoveryBackupPath(targetPath);
        try
        {
            await _fileSystem.CopyFileAsync(backupPath, rollbackTempPath, CancellationToken.None);
            await _fileSystem.ReplaceAsync(rollbackTempPath, targetPath, recoveryPath, CancellationToken.None);
            var restoredBytes = await _fileSystem.ReadAllBytesAsync(targetPath, CancellationToken.None);
            _ = await _fileSystem.ReadAllBytesAsync(recoveryPath, CancellationToken.None);
            return new RollbackResult(
                string.Equals(Fingerprint(restoredBytes), expectedFingerprint, StringComparison.Ordinal),
                recoveryPath);
        }
        catch (IOException)
        {
            return new RollbackResult(false, await FindExistingRecoveryPathAsync(recoveryPath));
        }
        catch (UnauthorizedAccessException)
        {
            return new RollbackResult(false, await FindExistingRecoveryPathAsync(recoveryPath));
        }
        finally
        {
            await DeleteBestEffortAsync(rollbackTempPath);
        }
    }

    private NutConfigurationChangePreview BuildPreview(
        NutConfigurationFileSnapshot snapshot,
        string candidateText,
        string candidateFingerprint)
    {
        var candidateLines = SplitLines(candidateText);
        var lines = new List<NutConfigurationPreviewLine>();
        for (var index = 0; index < snapshot.Document.Nodes.Count; index++)
        {
            var node = snapshot.Document.Nodes[index];
            if (!node.IsModified)
            {
                continue;
            }

            var sensitive = node switch
            {
                NutConfigurationAssignmentNode assignment => assignment.IsSensitive,
                NutConfigurationDirectiveNode directive => directive.IsSensitive,
                _ => false
            };
            lines.Add(new NutConfigurationPreviewLine(
                index + 1,
                sensitive ? RedactedText : node.RawText,
                sensitive ? RedactedText : candidateLines[index],
                sensitive));
        }

        return new NutConfigurationChangePreview(snapshot.TargetPath, candidateFingerprint, lines);
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var offset = 0;
        while (offset < text.Length)
        {
            var start = offset;
            while (offset < text.Length && text[offset] is not '\r' and not '\n')
            {
                offset++;
            }

            lines.Add(text[start..offset]);
            if (offset < text.Length && text[offset] == '\r' && offset + 1 < text.Length && text[offset + 1] == '\n')
            {
                offset += 2;
            }
            else if (offset < text.Length)
            {
                offset++;
            }
        }

        return lines;
    }

    private static bool MatchesOriginal(NutConfigurationFileSnapshot snapshot, ReadOnlySpan<byte> bytes) =>
        bytes.Length == snapshot.OriginalLength && string.Equals(Fingerprint(bytes), snapshot.OriginalFingerprint, StringComparison.Ordinal);

    private static string Fingerprint(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string CreateTemporaryPath(string targetPath) =>
        Path.Combine(GetDirectory(targetPath), $".{Path.GetFileName(targetPath)}.nutmanager-{Guid.NewGuid():N}.tmp");

    private static string CreateBackupPath(string targetPath) =>
        Path.Combine(GetDirectory(targetPath), $"{Path.GetFileName(targetPath)}.nutmanager-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.bak");

    private static string CreateRecoveryBackupPath(string targetPath) =>
        Path.Combine(GetDirectory(targetPath), $"{Path.GetFileName(targetPath)}.nutmanager-recovery-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.bak");

    private static string GetDirectory(string targetPath) =>
        Path.GetDirectoryName(targetPath) ?? throw new ArgumentException("A target path must include a directory.", nameof(targetPath));

    private async Task DeleteBestEffortAsync(string path)
    {
        try
        {
            await _fileSystem.DeleteFileIfExistsAsync(path, CancellationToken.None);
        }
        catch (IOException)
        {
            // Cleanup must never hide the primary failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup must never hide the primary failure.
        }
    }

    private async Task<string?> FindExistingRecoveryPathAsync(string recoveryPath)
    {
        try
        {
            return await _fileSystem.FileExistsAsync(recoveryPath, CancellationToken.None) ? recoveryPath : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class AcceptingCandidateValidator : INutConfigurationCandidateValidator
    {
        public static AcceptingCandidateValidator Instance { get; } = new();

        public Task<NutConfigurationValidationResult> ValidateAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken) =>
            Task.FromResult(NutConfigurationValidationResult.Success());
    }

    private sealed class AcceptingPostApplyValidator : INutConfigurationPostApplyValidator
    {
        public static AcceptingPostApplyValidator Instance { get; } = new();

        public Task<NutConfigurationValidationResult> ValidateAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken) =>
            Task.FromResult(NutConfigurationValidationResult.Success());
    }

    private readonly record struct RollbackResult(bool Succeeded, string? RecoveryPath);
}
