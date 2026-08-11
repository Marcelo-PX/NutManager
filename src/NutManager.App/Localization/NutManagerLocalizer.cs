using System.Globalization;
using System.Resources;
using System.Collections;
using NutManager.Core.Models;

namespace NutManager.App.Localization;

public sealed class NutManagerLocalizer
{
    private static readonly ResourceManager ResourceManager = new("NutManager.App.Localization.Strings", typeof(NutManagerLocalizer).Assembly);

    public static IReadOnlyCollection<string> RequiredKeys { get; } =
        GetAvailableKeys(UiLanguagePreference.PtBr).OrderBy(key => key, StringComparer.Ordinal).ToArray();

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

    public static IReadOnlySet<string> GetAvailableKeys(UiLanguagePreference language)
    {
        var resources = ResourceManager.GetResourceSet(CultureFor(language), true, false);
        return resources is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : resources.Cast<DictionaryEntry>()
                .Select(entry => (string)entry.Key)
                .ToHashSet(StringComparer.Ordinal);
    }

    private CultureInfo Culture => CultureFor(Language);

    private static CultureInfo CultureFor(UiLanguagePreference language) =>
        CultureInfo.GetCultureInfo(language == UiLanguagePreference.EnUs ? "en-US" : "pt-BR");
}
