using System.Text.Json;
using NutManager.App.ViewModels;

namespace NutManager.App.Services;

public sealed class ThemePreferenceStore
{
    private const string FileName = "theme-preference.json";
    private readonly string? _filePath;

    public ThemePreferenceStore()
    {
        var applicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _filePath = string.IsNullOrWhiteSpace(applicationDataPath)
            ? null
            : Path.Combine(applicationDataPath, "NutManager", FileName);
    }

    public ThemePreference Load()
    {
        if (_filePath is null || !File.Exists(_filePath))
        {
            return ThemePreference.System;
        }

        try
        {
            var preference = JsonSerializer.Deserialize<ThemePreference>(File.ReadAllText(_filePath));
            return Enum.IsDefined(preference) ? preference : ThemePreference.System;
        }
        catch (IOException)
        {
            return ThemePreference.System;
        }
        catch (JsonException)
        {
            return ThemePreference.System;
        }
        catch (UnauthorizedAccessException)
        {
            return ThemePreference.System;
        }
    }

    public void Save(ThemePreference preference)
    {
        if (_filePath is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(preference));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
