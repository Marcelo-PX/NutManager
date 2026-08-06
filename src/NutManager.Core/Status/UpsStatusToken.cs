namespace NutManager.Core.Status;

public sealed record UpsStatusToken(
    string OriginalToken,
    StatusSemanticState State,
    StatusSeverity Severity,
    bool IsKnown);
