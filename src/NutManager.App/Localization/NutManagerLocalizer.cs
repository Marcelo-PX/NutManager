using System.Globalization;
using System.Resources;
using NutManager.Core.Models;

namespace NutManager.App.Localization;

public sealed class NutManagerLocalizer
{
    private static readonly ResourceManager ResourceManager = new("NutManager.App.Localization.Strings", typeof(NutManagerLocalizer).Assembly);

    public static IReadOnlyCollection<string> RequiredKeys { get; } =
    [
        "App.Name", "Nav.Overview", "Nav.Devices", "Nav.Administration", "Nav.Diagnostics", "Nav.Settings",
        "Shell.ToggleNavigation", "Shell.ToggleTheme", "Shell.SimulationActive", "Shell.ReviewChanges",
        "Status.Connected", "Status.Connecting", "Status.Reconnecting", "Status.Disconnected", "Status.ConnectionFailed", "Status.Stale", "Status.Unavailable",
        "Appearance.Title", "Appearance.Theme", "Appearance.Language", "Appearance.Sidebar", "Appearance.RestartRequired", "Appearance.SaveError",
        "Theme.System", "Theme.Light", "Theme.Dark", "Language.PtBr", "Language.EnUs", "Sidebar.Expanded", "Sidebar.Collapsed"
    ];

    public NutManagerLocalizer(UiLanguagePreference language) => Language = language;

    public UiLanguagePreference Language { get; }

    public string Get(string key) =>
        ResourceManager.GetString(key, Culture) ??
        ResourceManager.GetString(key, CultureInfo.GetCultureInfo("pt-BR")) ??
        key;

    public static bool HasRequiredKeys(UiLanguagePreference language)
    {
        var culture = CultureFor(language);
        return RequiredKeys.All(key => ResourceManager.GetString(key, culture) is not null);
    }

    private CultureInfo Culture => CultureFor(Language);

    private static CultureInfo CultureFor(UiLanguagePreference language) =>
        CultureInfo.GetCultureInfo(language == UiLanguagePreference.EnUs ? "en-US" : "pt-BR");
}
