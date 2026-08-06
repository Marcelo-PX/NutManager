namespace NutManager.Core.Models;

public sealed record DiagnosticResult
{
    public DiagnosticResult(
        string code,
        DiagnosticSeverity severity,
        string summary,
        DateTimeOffset timestamp,
        string? technicalDetails = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        Code = code;
        Severity = severity;
        Summary = summary;
        Timestamp = timestamp;
        TechnicalDetails = technicalDetails;
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    public string Summary { get; }

    public string? TechnicalDetails { get; }

    public DateTimeOffset Timestamp { get; }
}
