namespace NutManager.Core.Configuration;

public enum NutConfigurationTextEncoding
{
    Utf8,
    Utf8Bom,
    Utf16LittleEndian,
    Utf16BigEndian
}

public enum NutConfigurationLoadStatus
{
    Success,
    TargetNotFound,
    UnsupportedEncoding,
    AccessDenied,
    Cancelled,
    Failed
}

public enum NutConfigurationApplyStatus
{
    Success,
    NoChanges,
    TargetNotFound,
    ChangedExternally,
    ChangedExternallyRollbackFailed,
    CandidateValidationFailed,
    TempWriteFailed,
    ReplaceFailed,
    PostApplyValidationFailedRolledBack,
    PostApplyValidationFailedRollbackFailed,
    VerificationFailedRolledBack,
    VerificationFailedRollbackFailed,
    RemoteCommitOutcomeUnknown,
    Cancelled,
    Failed
}

public sealed class NutConfigurationFileSnapshot
{
    public NutConfigurationFileSnapshot(
        string targetPath,
        NutConfigurationFileKind fileKind,
        NutConfigurationDocument document,
        NutConfigurationTextEncoding encoding,
        string originalFingerprint,
        long originalLength)
    {
        TargetPath = targetPath;
        FileKind = fileKind;
        Document = document;
        Encoding = encoding;
        OriginalFingerprint = originalFingerprint;
        OriginalLength = originalLength;
    }

    public string TargetPath { get; }

    public NutConfigurationFileKind FileKind { get; }

    public NutConfigurationDocument Document { get; }

    public NutConfigurationTextEncoding Encoding { get; }

    public string OriginalFingerprint { get; }

    public long OriginalLength { get; }
}

public sealed class NutConfigurationLoadResult
{
    public NutConfigurationLoadResult(NutConfigurationLoadStatus status, NutConfigurationFileSnapshot? snapshot = null, string? message = null)
    {
        Status = status;
        Snapshot = snapshot;
        Message = message;
    }

    public NutConfigurationLoadStatus Status { get; }

    public NutConfigurationFileSnapshot? Snapshot { get; }

    public string? Message { get; }
}

public sealed class NutConfigurationPreviewLine
{
    public NutConfigurationPreviewLine(int lineNumber, string originalText, string candidateText, bool isRedacted)
    {
        LineNumber = lineNumber;
        OriginalText = originalText;
        CandidateText = candidateText;
        IsRedacted = isRedacted;
    }

    public int LineNumber { get; }

    public string OriginalText { get; }

    public string CandidateText { get; }

    public bool IsRedacted { get; }
}

public sealed class NutConfigurationChangePreview
{
    public NutConfigurationChangePreview(string targetPath, string candidateFingerprint, IReadOnlyList<NutConfigurationPreviewLine> lines)
    {
        TargetPath = targetPath;
        CandidateFingerprint = candidateFingerprint;
        Lines = lines;
    }

    public string TargetPath { get; }

    public string CandidateFingerprint { get; }

    public IReadOnlyList<NutConfigurationPreviewLine> Lines { get; }

    public bool HasChanges => Lines.Count > 0;

    public int ChangeCount => Lines.Count;
}

public sealed class NutConfigurationPreparedChange
{
    public NutConfigurationPreparedChange(
        NutConfigurationFileSnapshot snapshot,
        string candidateText,
        ReadOnlyMemory<byte> candidateBytes,
        string candidateFingerprint,
        NutConfigurationChangePreview preview)
    {
        Snapshot = snapshot;
        CandidateText = candidateText;
        CandidateBytes = candidateBytes;
        CandidateFingerprint = candidateFingerprint;
        Preview = preview;
    }

    public NutConfigurationFileSnapshot Snapshot { get; }

    public string CandidateText { get; }

    public ReadOnlyMemory<byte> CandidateBytes { get; }

    public string CandidateFingerprint { get; }

    public NutConfigurationChangePreview Preview { get; }

    public bool HasChanges => Snapshot.Document.IsModified;
}

public sealed class NutConfigurationValidationResult
{
    private NutConfigurationValidationResult(bool isValid, string? message)
    {
        IsValid = isValid;
        Message = message;
    }

    public bool IsValid { get; }

    public string? Message { get; }

    public static NutConfigurationValidationResult Success() => new(true, null);

    public static NutConfigurationValidationResult Failure(string? message = null) => new(false, message);
}

public interface INutConfigurationCandidateValidator
{
    Task<NutConfigurationValidationResult> ValidateAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken);
}

public interface INutConfigurationPostApplyValidator
{
    Task<NutConfigurationValidationResult> ValidateAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken);
}

public sealed class NutConfigurationApplyResult
{
    public NutConfigurationApplyResult(
        NutConfigurationApplyStatus status,
        string? backupPath = null,
        string? message = null,
        bool rollbackSucceeded = false,
        string? recoveryPath = null)
    {
        Status = status;
        BackupPath = backupPath;
        Message = message;
        RollbackSucceeded = rollbackSucceeded;
        RecoveryPath = recoveryPath;
    }

    public NutConfigurationApplyStatus Status { get; }

    public string? BackupPath { get; }

    public string? Message { get; }

    public bool RollbackSucceeded { get; }

    public string? RecoveryPath { get; }
}
