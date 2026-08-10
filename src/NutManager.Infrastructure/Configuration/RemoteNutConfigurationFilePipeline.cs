using System.Security.Cryptography;
using System.Text;
using NutManager.Core.Configuration;
using NutManager.Core.Services;
using NutManager.Infrastructure.Remote.Ssh;

namespace NutManager.Infrastructure.Configuration;

/// <summary>
/// Reuses the syntax-preserving configuration model for a validated remote directory.
/// The remote session owns all SFTP and fixed Windows commit operations.
/// </summary>
public sealed class RemoteNutConfigurationFilePipeline : INutConfigurationFilePipeline
{
    private const string RedactedText = "<redacted>";
    private readonly IRemoteNutManagementSession _session;
    private readonly string _configurationDirectory;
    private readonly NutConfigurationParser _parser;
    private readonly bool _canWrite;

    public RemoteNutConfigurationFilePipeline(
        IRemoteNutManagementSession session,
        string configurationDirectory,
        bool canWrite,
        NutConfigurationParser? parser = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _configurationDirectory = RemotePathMapper.ToSftpPath(configurationDirectory);
        _canWrite = canWrite;
        _parser = parser ?? new NutConfigurationParser();
    }

    public async Task<NutConfigurationLoadResult> LoadAsync(string targetPath, NutConfigurationFileKind fileKind, CancellationToken cancellationToken = default)
    {
        var expectedPath = GetTargetPath(fileKind);
        if (!string.Equals(RemotePathMapper.ToSftpPath(targetPath), expectedPath, StringComparison.Ordinal))
        {
            throw new ArgumentException("The remote configuration target is not recognized.", nameof(targetPath));
        }

        try
        {
            var read = await _session.ReadFileAsync(expectedPath, cancellationToken);
            if (read.Status != RemoteNutTransportStatus.Success)
            {
                return new NutConfigurationLoadResult(ToLoadStatus(read.Status), message: read.Message);
            }

            var (encoding, text) = NutConfigurationTextCodec.Decode(read.Bytes.Span);
            var document = _parser.Parse(fileKind, text);
            return new NutConfigurationLoadResult(
                NutConfigurationLoadStatus.Success,
                new NutConfigurationFileSnapshot(expectedPath, fileKind, document, encoding, Fingerprint(read.Bytes.Span), read.Bytes.Length));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new NutConfigurationLoadResult(NutConfigurationLoadStatus.Cancelled, message: "Remote configuration load was cancelled.");
        }
        catch (DecoderFallbackException)
        {
            return new NutConfigurationLoadResult(NutConfigurationLoadStatus.UnsupportedEncoding, message: "Remote configuration encoding is not supported.");
        }
        catch
        {
            return new NutConfigurationLoadResult(NutConfigurationLoadStatus.Failed, message: "Remote configuration could not be read.");
        }
    }

    public NutConfigurationPreparedChange Prepare(NutConfigurationFileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var candidateText = snapshot.Document.Serialize();
        var candidateBytes = NutConfigurationTextCodec.Encode(candidateText, snapshot.Encoding);
        var fingerprint = Fingerprint(candidateBytes);
        var candidateLines = SplitLines(candidateText);
        var lines = new List<NutConfigurationPreviewLine>();
        for (var index = 0; index < snapshot.Document.Nodes.Count; index++)
        {
            var node = snapshot.Document.Nodes[index];
            if (!node.IsModified)
            {
                continue;
            }

            var sensitive = node is NutConfigurationAssignmentNode assignment && assignment.IsSensitive ||
                node is NutConfigurationDirectiveNode directive && directive.IsSensitive;
            lines.Add(new NutConfigurationPreviewLine(index + 1, sensitive ? RedactedText : node.RawText, sensitive ? RedactedText : candidateLines[index], sensitive));
        }

        return new NutConfigurationPreparedChange(snapshot, candidateText, candidateBytes, fingerprint, new NutConfigurationChangePreview(snapshot.TargetPath, fingerprint, lines));
    }

    public async Task<NutConfigurationApplyResult> ApplyAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!change.HasChanges)
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.NoChanges, message: "Configuration has no changes to apply.");
        }

        if (!_canWrite || _session.Platform != RemoteNutPlatform.Windows)
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Failed, message: "Remote configuration writing is available only after a verified Windows safe-write capability probe.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Cancelled, message: "Remote configuration apply was cancelled.");
        }

        var fileName = RemoteNutConfigurationFiles.GetFileName(change.Snapshot.FileKind);
        if (!string.Equals(change.Snapshot.TargetPath, GetTargetPath(change.Snapshot.FileKind), StringComparison.Ordinal))
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Failed, message: "Remote configuration target is invalid.");
        }

        var current = await _session.ReadFileAsync(change.Snapshot.TargetPath, cancellationToken);
        if (current.Status != RemoteNutTransportStatus.Success)
        {
            return new NutConfigurationApplyResult(ToApplyStatus(current.Status), message: current.Message);
        }

        if (!MatchesOriginal(change.Snapshot, current.Bytes.Span))
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.ChangedExternally, message: "Remote configuration changed externally.");
        }

        var temporaryName = $".nutmanager-{fileName}-{Guid.NewGuid():N}.tmp";
        var backupName = $".nutmanager-{fileName}-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.bak";
        var uploaded = await _session.UploadCandidateAsync(
            new RemoteNutCandidateUploadRequest(_configurationDirectory, fileName, temporaryName, change.CandidateBytes),
            cancellationToken);
        if (uploaded.Status != RemoteNutTransportStatus.Success || !uploaded.Bytes.Span.SequenceEqual(change.CandidateBytes.Span) || !ValidateCandidate(uploaded.Bytes.Span, change))
        {
            var failure = new NutConfigurationApplyResult(NutConfigurationApplyStatus.TempWriteFailed, message: "Remote candidate upload could not be verified.");
            return uploaded.Status == RemoteNutTransportStatus.Success
                ? await CleanupCandidateAfterAbortAsync(failure, temporaryName)
                : failure;
        }

        current = await _session.ReadFileAsync(change.Snapshot.TargetPath, cancellationToken);
        if (current.Status != RemoteNutTransportStatus.Success || !MatchesOriginal(change.Snapshot, current.Bytes.Span))
        {
            return await CleanupCandidateAfterAbortAsync(
                new NutConfigurationApplyResult(NutConfigurationApplyStatus.ChangedExternally, message: "Remote configuration changed externally."),
                temporaryName);
        }

        var commit = await _session.CommitWindowsConfigurationAsync(
            new RemoteNutWindowsCommitRequest(
                _configurationDirectory,
                fileName,
                temporaryName,
                backupName,
                change.Snapshot.OriginalFingerprint,
                change.CandidateFingerprint),
            CancellationToken.None);
        if (commit.Status == RemoteNutTransportStatus.OutcomeUnknown)
        {
            return new NutConfigurationApplyResult(
                NutConfigurationApplyStatus.RemoteCommitOutcomeUnknown,
                commit.BackupPath,
                "The remote configuration commit outcome could not be confirmed.",
                temporaryPath: RemotePathMapper.Combine(_configurationDirectory, temporaryName));
        }

        if (commit.Status != RemoteNutTransportStatus.Success)
        {
            return await CleanupCandidateAfterAbortAsync(
                new NutConfigurationApplyResult(NutConfigurationApplyStatus.ReplaceFailed, commit.BackupPath, commit.Message),
                temporaryName);
        }

        var backupPath = commit.BackupPath ?? RemotePathMapper.Combine(_configurationDirectory, backupName);
        var target = await _session.ReadFileAsync(change.Snapshot.TargetPath, CancellationToken.None);
        var backup = await _session.ReadFileAsync(backupPath, CancellationToken.None);
        if (target.Status == RemoteNutTransportStatus.Success && backup.Status == RemoteNutTransportStatus.Success &&
            target.Bytes.Span.SequenceEqual(change.CandidateBytes.Span) && MatchesOriginal(change.Snapshot, backup.Bytes.Span))
        {
            return new NutConfigurationApplyResult(NutConfigurationApplyStatus.Success, backupPath);
        }

        return await RollbackAsync(change, fileName, backupName, backupPath);
    }

    private async Task<NutConfigurationApplyResult> RollbackAsync(NutConfigurationPreparedChange change, string fileName, string backupName, string backupPath)
    {
        var rollbackName = $".nutmanager-{fileName}-{Guid.NewGuid():N}.tmp";
        var recoveryName = $".nutmanager-recovery-{fileName}-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.bak";
        var rollback = await _session.RollbackWindowsConfigurationAsync(
            new RemoteNutWindowsRollbackRequest(_configurationDirectory, fileName, backupName, rollbackName, recoveryName, change.Snapshot.OriginalFingerprint),
            CancellationToken.None);
        var recoveryPath = rollback.RecoveryPath ?? RemotePathMapper.Combine(_configurationDirectory, recoveryName);
        if (rollback.Status == RemoteNutTransportStatus.Success)
        {
            var restored = await _session.ReadFileAsync(change.Snapshot.TargetPath, CancellationToken.None);
            var recovery = await _session.ReadFileAsync(recoveryPath, CancellationToken.None);
            if (restored.Status == RemoteNutTransportStatus.Success && MatchesOriginal(change.Snapshot, restored.Bytes.Span) && recovery.Status == RemoteNutTransportStatus.Success)
            {
                return new NutConfigurationApplyResult(NutConfigurationApplyStatus.VerificationFailedRolledBack, backupPath, "Remote configuration verification failed and the original was restored.", true, recoveryPath);
            }
        }

        return new NutConfigurationApplyResult(NutConfigurationApplyStatus.VerificationFailedRollbackFailed, backupPath, "Remote configuration may require manual recovery.", false, rollback.RecoveryPath);
    }

    private async Task<NutConfigurationApplyResult> CleanupCandidateAfterAbortAsync(NutConfigurationApplyResult originalResult, string temporaryName)
    {
        var temporaryPath = RemotePathMapper.Combine(_configurationDirectory, temporaryName);
        try
        {
            var cleanup = await _session.DeleteGeneratedTemporaryFileAsync(_configurationDirectory, temporaryName, CancellationToken.None);
            if (cleanup.IsClean)
            {
                return originalResult;
            }

            return new NutConfigurationApplyResult(
                NutConfigurationApplyStatus.RemoteTemporaryCleanupFailed,
                originalResult.BackupPath,
                "The remote temporary candidate cleanup could not be confirmed.",
                originalResult.RollbackSucceeded,
                originalResult.RecoveryPath,
                temporaryPath);
        }
        catch
        {
            return new NutConfigurationApplyResult(
                NutConfigurationApplyStatus.RemoteTemporaryCleanupFailed,
                originalResult.BackupPath,
                "The remote temporary candidate cleanup could not be confirmed.",
                originalResult.RollbackSucceeded,
                originalResult.RecoveryPath,
                temporaryPath);
        }
    }

    private string GetTargetPath(NutConfigurationFileKind fileKind) => RemotePathMapper.Combine(_configurationDirectory, RemoteNutConfigurationFiles.GetFileName(fileKind));

    private bool ValidateCandidate(ReadOnlySpan<byte> bytes, NutConfigurationPreparedChange change)
    {
        try
        {
            var (encoding, text) = NutConfigurationTextCodec.Decode(bytes);
            return encoding == change.Snapshot.Encoding &&
                string.Equals(text, change.CandidateText, StringComparison.Ordinal) &&
                _parser.Parse(change.Snapshot.FileKind, text).Serialize() == text;
        }
        catch
        {
            return false;
        }
    }

    private static NutConfigurationLoadStatus ToLoadStatus(RemoteNutTransportStatus status) => status switch
    {
        RemoteNutTransportStatus.NotFound => NutConfigurationLoadStatus.TargetNotFound,
        RemoteNutTransportStatus.AccessDenied => NutConfigurationLoadStatus.AccessDenied,
        RemoteNutTransportStatus.Cancelled => NutConfigurationLoadStatus.Cancelled,
        _ => NutConfigurationLoadStatus.Failed
    };

    private static NutConfigurationApplyStatus ToApplyStatus(RemoteNutTransportStatus status) => status switch
    {
        RemoteNutTransportStatus.NotFound => NutConfigurationApplyStatus.TargetNotFound,
        RemoteNutTransportStatus.Cancelled => NutConfigurationApplyStatus.Cancelled,
        RemoteNutTransportStatus.OutcomeUnknown => NutConfigurationApplyStatus.RemoteCommitOutcomeUnknown,
        _ => NutConfigurationApplyStatus.Failed
    };

    private static bool MatchesOriginal(NutConfigurationFileSnapshot snapshot, ReadOnlySpan<byte> bytes) =>
        snapshot.OriginalLength == bytes.Length && string.Equals(snapshot.OriginalFingerprint, Fingerprint(bytes), StringComparison.Ordinal);

    private static string Fingerprint(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var offset = 0;
        while (offset < text.Length)
        {
            var start = offset;
            while (offset < text.Length && text[offset] is not '\r' and not '\n') offset++;
            lines.Add(text[start..offset]);
            if (offset < text.Length && text[offset] == '\r' && offset + 1 < text.Length && text[offset + 1] == '\n') offset += 2;
            else if (offset < text.Length) offset++;
        }

        return lines;
    }
}
