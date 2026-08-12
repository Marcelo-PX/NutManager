using NutManager.App.Localization;
using NutManager.Core.Configuration.Semantic;

namespace NutManager.App.ViewModels;

public sealed record SemanticReviewItemViewModel(
    string Label,
    string Operation,
    string? Section,
    string? PreviousValue,
    string? NewValue,
    string Activation,
    bool Sensitive);

public sealed record SemanticValidationIssueViewModel(string Message, string Target, string? Section);

public sealed record GeneratedPreviewLineViewModel(int LineNumber, string OriginalText, string CandidateText, bool IsRedacted);

public sealed record SemanticCustomParameterViewModel(
    string Name,
    string? SafeValue,
    string? Section,
    string Warning,
    bool Sensitive);

/// <summary>
/// Read-only presentation boundary for T25 semantic review. It exposes neither the
/// generated candidate text nor an apply/write command.
/// </summary>
public sealed class SemanticConfigurationReviewViewModel
{
    public SemanticConfigurationReviewViewModel(
        NutConfigurationGeneratedPreview generated,
        NutConfigurationSemanticProjection projection,
        NutManagerLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(localizer);
        Items = generated.SemanticReview.Changes.Select(change => new SemanticReviewItemViewModel(
            LocalizeOrTechnical(localizer, change.LabelResourceKey, change.SemanticId),
            localizer.Get(OperationKey(change.Operation)),
            change.Section,
            change.Sensitive ? null : LocalizeSemanticValue(localizer, change.OldSafeDisplayValue),
            change.Sensitive ? localizer.Get(change.Operation == NutConfigurationSemanticChangeOperation.Remove
                ? "Semantic.Sensitive.RemovalPending" : "Semantic.Sensitive.ReplacementPending") : LocalizeSemanticValue(localizer, change.NewSafeDisplayValue),
            localizer.Get(ActivationKey(change.Activation)),
            change.Sensitive)).ToArray();
        ValidationIssues = generated.Validation.Issues.Select(issue => new SemanticValidationIssueViewModel(
            localizer.Get(issue.ResourceKey), issue.SemanticTarget, issue.Section)).ToArray();
        PreviewLines = generated.PreparedChange.Preview.Lines.Select(line => new GeneratedPreviewLineViewModel(
            line.LineNumber, line.OriginalText, line.CandidateText, line.IsRedacted)).ToArray();
        CustomParameters = projection.CustomParameters.Select(parameter => new SemanticCustomParameterViewModel(
            parameter.Name, parameter.SafeValue, parameter.Section, localizer.Get("Semantic.Custom.LimitedValidation"), parameter.Sensitive)).ToArray();
        PendingText = string.Format(localizer.Get("Semantic.Review.PendingCount"), Items.Count);
        FileName = Path.GetFileName(generated.PreparedChange.Preview.TargetPath);
        BackupNotice = localizer.Get("Administration.Configuration.BackupNotice");
        ChangesTitle = localizer.Get("Semantic.Review.Changes");
        ValidationTitle = localizer.Get("Semantic.Validation.Title");
        PreviewTitle = localizer.Get("Semantic.Preview.Title");
        CustomParametersTitle = localizer.Get("Semantic.Custom.Title");
    }

    public IReadOnlyList<SemanticReviewItemViewModel> Items { get; }
    public IReadOnlyList<SemanticValidationIssueViewModel> ValidationIssues { get; }
    public IReadOnlyList<GeneratedPreviewLineViewModel> PreviewLines { get; }
    public IReadOnlyList<SemanticCustomParameterViewModel> CustomParameters { get; }
    public string PendingText { get; }

    /// <summary>Target file name shown next to each pending change; the full path stays in the pipeline.</summary>
    public string FileName { get; }

    /// <summary>Existing T14 backup/rollback notice reused verbatim; no new safety claim is introduced.</summary>
    public string BackupNotice { get; }

    public int ChangeCount => Items.Count;
    public string ChangesTitle { get; }
    public string ValidationTitle { get; }
    public string PreviewTitle { get; }
    public string CustomParametersTitle { get; }
    public bool HasChanges => Items.Count > 0;
    public bool HasValidationIssues => ValidationIssues.Count > 0;
    public bool HasPreviewLines => PreviewLines.Count > 0;
    public bool HasCustomParameters => CustomParameters.Count > 0;

    private static string OperationKey(NutConfigurationSemanticChangeOperation operation) => $"Semantic.Operation.{operation}";
    private static string ActivationKey(NutConfigurationActivation activation) => $"Semantic.Activation.{activation}";
    private static string LocalizeOrTechnical(NutManagerLocalizer localizer, string resourceKey, string fallback)
    {
        var value = localizer.Get(resourceKey);
        return string.Equals(value, resourceKey, StringComparison.Ordinal) ? fallback : value;
    }

    private static string? LocalizeSemanticValue(NutManagerLocalizer localizer, string? value) =>
        value?.StartsWith("Semantic.", StringComparison.Ordinal) == true ? localizer.Get(value) : value;
}
