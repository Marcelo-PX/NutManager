using NutManager.Core.Models;

namespace NutManager.App.ViewModels;

public sealed record ThemeOption(ThemePreference Preference, string Title);

public sealed record PresentationOption<T>(T Value, string Title) where T : struct, Enum;
