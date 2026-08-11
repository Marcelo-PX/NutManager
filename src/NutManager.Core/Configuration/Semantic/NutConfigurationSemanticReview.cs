namespace NutManager.Core.Configuration.Semantic;

public enum NutConfigurationSemanticChangeOperation
{
    Set,
    Add,
    Remove,
    SetAutomatic,
    AddRepeatedRow,
    RemoveRepeatedRow,
    AddSection,
    RemoveSection,
    RenameSection,
    AddCustomParameter,
    EditCustomParameter,
    RemoveCustomParameter
}

public sealed record NutConfigurationSemanticReviewItem(
    NutConfigurationFileKind FileKind,
    string SemanticId,
    string LabelResourceKey,
    NutConfigurationSemanticChangeOperation Operation,
    string? Section,
    string? RowId,
    NutConfigurationSemanticState? PreviousState,
    NutConfigurationSemanticState? NewState,
    string? OldSafeDisplayValue,
    string? NewSafeDisplayValue,
    bool Sensitive,
    NutConfigurationActivation Activation);

public sealed record NutConfigurationSemanticReview(IReadOnlyList<NutConfigurationSemanticReviewItem> Changes)
{
    public int ChangeCount => Changes.Count;
    public bool HasChanges => Changes.Count > 0;
}

public sealed class NutSensitiveValue : IDisposable
{
    private char[]? _value;

    public NutSensitiveValue(ReadOnlySpan<char> value)
    {
        if (value.Length == 0) throw new ArgumentException("A replacement value is required.", nameof(value));
        _value = value.ToArray();
    }

    internal char[] CopyValue() => _value?.ToArray() ?? throw new ObjectDisposedException(nameof(NutSensitiveValue));

    public void Dispose()
    {
        if (_value is null) return;
        Array.Clear(_value);
        _value = null;
    }

    public override string ToString() => "<sensitive>";
}

public sealed record NutConfigurationGeneratedPreview(
    NutConfigurationSemanticReview SemanticReview,
    NutConfigurationSemanticValidationResult Validation,
    NutConfigurationPreparedChange PreparedChange);

public static class NutConfigurationGeneratedPreviewFactory
{
    public static NutConfigurationGeneratedPreview Prepare(
        INutConfigurationFilePipeline pipeline,
        NutConfigurationFileSnapshot originalSnapshot,
        NutConfigurationSemanticDraft draft)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(originalSnapshot);
        ArgumentNullException.ThrowIfNull(draft);
        if (originalSnapshot.FileKind != draft.FileKind) throw new ArgumentException("The draft file kind does not match the snapshot.", nameof(draft));

        var candidateDocument = draft.Materialize();
        var candidateSnapshot = new NutConfigurationFileSnapshot(
            originalSnapshot.TargetPath,
            originalSnapshot.FileKind,
            candidateDocument,
            originalSnapshot.Encoding,
            originalSnapshot.OriginalFingerprint,
            originalSnapshot.OriginalLength);
        return new(draft.Review, draft.Validation, pipeline.Prepare(candidateSnapshot));
    }
}
