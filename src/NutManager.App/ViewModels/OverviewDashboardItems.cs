using System.Windows.Input;

namespace NutManager.App.ViewModels;

/// <summary>Label/value row used by the Overview active-configuration card.</summary>
public sealed record OverviewInfoRowViewModel(string Label, string Value, bool IsAccent = true);

/// <summary>
/// Navigation-only shortcut shown on the Overview administration card. It carries an existing
/// navigation command; it never performs an administrative action itself.
/// </summary>
public sealed record OverviewShortcutViewModel(
    string Title,
    string Description,
    OverviewShortcutGlyph Glyph,
    ICommand Command)
{
    public bool IsConfiguration => Glyph == OverviewShortcutGlyph.Configuration;
    public bool IsService => Glyph == OverviewShortcutGlyph.Service;
    public bool IsDevices => Glyph == OverviewShortcutGlyph.Devices;
    public bool IsDiagnostics => Glyph == OverviewShortcutGlyph.Diagnostics;
}

public enum OverviewShortcutGlyph
{
    Configuration,
    Service,
    Devices,
    Diagnostics
}
