namespace NutManager.App.ViewModels;

public sealed record OverviewStatusItemViewModel(
    string OriginalToken,
    string StateText,
    string SeverityText,
    bool IsUnknown = false);
