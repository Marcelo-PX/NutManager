namespace NutManager.App.ViewModels;

/// <summary>
/// Presentation-only grouping of configuration fields into the sections shown by the graphical
/// form. Grouping uses the schema's existing presentation metadata; no field is invented, hidden
/// or reordered beyond its declared insertion order.
/// </summary>
public sealed record UpsFieldGroupViewModel(
    string GroupKey,
    string Title,
    IReadOnlyList<UpsConfigurationFieldViewModel> Fields)
{
    public bool IsIdentity => GroupKey == "Ups.Group.Identity";
    public bool IsConnection => GroupKey == "Ups.Group.Connection";
    public bool IsBattery => GroupKey == "Ups.Group.Battery";
    public bool IsRuntime => GroupKey == "Ups.Group.Runtime";
    public bool IsBehavior => GroupKey == "Ups.Group.Behavior";
    public bool IsDeviceMatch => GroupKey == "Ups.Group.DeviceMatch";
    public bool IsSecurity => GroupKey == "Ups.Group.Security";

    /// <summary>True when no more specific glyph applies, so the generic driver glyph is used.</summary>
    public bool IsGeneric => !IsIdentity && !IsConnection && !IsBattery && !IsRuntime &&
        !IsBehavior && !IsDeviceMatch && !IsSecurity;

    public static IReadOnlyList<UpsFieldGroupViewModel> From(IEnumerable<UpsConfigurationFieldViewModel> fields) =>
        fields.GroupBy(field => field.GroupKey, StringComparer.Ordinal)
            .Select(group => new UpsFieldGroupViewModel(group.Key, group.First().Group, group.ToArray()))
            .ToArray();
}
