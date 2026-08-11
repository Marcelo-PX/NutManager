namespace NutManager.Core.Validation;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record FieldValidationIssue(
    string Field,
    string Code,
    ValidationSeverity Severity,
    string ResourceKey);

public sealed record FieldValidationResult<T>(T? Value, IReadOnlyList<FieldValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool IsValid => !HasErrors;
}
