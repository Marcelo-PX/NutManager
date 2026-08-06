namespace NutManager.App.ViewModels;

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public sealed record ThemeOption(ThemePreference Preference, string Title);
