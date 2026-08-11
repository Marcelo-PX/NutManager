using NutManager.Core.Validation;

namespace NutManager.Core.Configuration.Semantic;

public interface INutConfigurationFieldValidationRule
{
    IReadOnlyList<NutConfigurationSemanticIssue> Validate(NutConfigurationSemanticField field);
}

public interface INutConfigurationCrossFieldValidationRule
{
    IReadOnlyList<NutConfigurationSemanticIssue> Validate(NutConfigurationSemanticProjection projection);
}

public interface INutConfigurationDocumentValidationRule
{
    IReadOnlyList<NutConfigurationSemanticIssue> Validate(NutConfigurationDocument document, NutConfigurationSemanticProjection projection);
}

public sealed record NutConfigurationSemanticValidationResult(IReadOnlyList<NutConfigurationSemanticIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);
    public bool CanReview => !HasErrors;
}

public sealed class NutConfigurationSemanticValidator
{
    private readonly IReadOnlyList<INutConfigurationFieldValidationRule> _fieldRules;
    private readonly IReadOnlyList<INutConfigurationCrossFieldValidationRule> _crossFieldRules;
    private readonly IReadOnlyList<INutConfigurationDocumentValidationRule> _documentRules;

    public NutConfigurationSemanticValidator(
        IEnumerable<INutConfigurationFieldValidationRule>? fieldRules = null,
        IEnumerable<INutConfigurationCrossFieldValidationRule>? crossFieldRules = null,
        IEnumerable<INutConfigurationDocumentValidationRule>? documentRules = null)
    {
        _fieldRules = fieldRules?.ToArray() ?? [];
        _crossFieldRules = crossFieldRules?.ToArray() ?? [];
        _documentRules = documentRules?.ToArray() ?? [];
    }

    public NutConfigurationSemanticValidationResult Validate(
        NutConfigurationDocument document,
        NutConfigurationSemanticProjection projection)
    {
        var issues = new List<NutConfigurationSemanticIssue>(projection.Issues);
        issues.AddRange(projection.CustomParameters.Select(parameter => new NutConfigurationSemanticIssue(
            "Custom.LimitedValidation", ValidationSeverity.Warning, "Semantic.Custom.LimitedValidation", parameter.Name, parameter.Section, parameter.RowId)));
        foreach (var field in projection.Fields)
            foreach (var rule in _fieldRules)
                issues.AddRange(rule.Validate(field));
        foreach (var rule in _crossFieldRules) issues.AddRange(rule.Validate(projection));
        foreach (var rule in _documentRules) issues.AddRange(rule.Validate(document, projection));
        return new(issues.OrderBy(issue => issue.SemanticTarget, StringComparer.Ordinal)
            .ThenBy(issue => issue.Section, StringComparer.Ordinal).ThenBy(issue => issue.RowId, StringComparer.Ordinal).ToArray());
    }

    public static IReadOnlyList<NutConfigurationSemanticIssue> ValidateCustomParameter(
        NutConfigurationEntryKind entryKind,
        string name,
        string value,
        string? section)
    {
        _ = entryKind;
        var issues = new List<NutConfigurationSemanticIssue>();
        if (string.IsNullOrWhiteSpace(name) || name.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character is '=' or '[' or ']'))
            issues.Add(new("Custom.NameInvalid", ValidationSeverity.Error, "Semantic.Custom.Validation.Name", "CustomParameter", section));
        if (value.Any(character => character is '\r' or '\n' || char.IsControl(character)))
            issues.Add(new("Custom.ValueInvalid", ValidationSeverity.Error, "Semantic.Custom.Validation.Value", "CustomParameter", section));
        if (section is not null && (string.IsNullOrWhiteSpace(section) || section.Any(character => character is '\r' or '\n' || char.IsControl(character))))
            issues.Add(new("Custom.SectionInvalid", ValidationSeverity.Error, "Semantic.Custom.Validation.Section", "CustomParameter", section));
        if (issues.Count == 0)
            issues.Add(new("Custom.LimitedValidation", ValidationSeverity.Warning, "Semantic.Custom.LimitedValidation", "CustomParameter", section));
        return issues;
    }
}
