using System.Text.Json;
using System.Text.Json.Serialization;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Persistence;

public sealed class JsonApplicationSettingsStore : IApplicationSettingsStore
{
    private const string FileName = "settings.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public JsonApplicationSettingsStore(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NutManager");
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ApplicationSettingsPersistenceException("The application settings directory is unavailable.");
        }

        _settingsPath = Path.Combine(root, FileName);
    }

    public string SettingsPath => _settingsPath;

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_settingsPath))
        {
            return new ApplicationSettings();
        }

        try
        {
            await using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(stream, SerializerOptions, cancellationToken)
                ?? throw new ApplicationSettingsPersistenceException("The settings JSON is empty.");
            return document.ToSettings();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ApplicationSettingsPersistenceException("The settings JSON is malformed.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new ApplicationSettingsPersistenceException("The settings values are invalid.", exception);
        }
        catch (IOException exception)
        {
            throw new ApplicationSettingsPersistenceException("The settings file could not be read.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ApplicationSettingsPersistenceException("The settings file could not be accessed.", exception);
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        _ = new ApplicationSettings(settings.SchemaVersion, settings.Host, settings.Port, settings.PreferredUpsName,
            settings.PollingInterval, settings.ConnectionTimeout, settings.Theme, settings.MockMode);

        var directory = Path.GetDirectoryName(_settingsPath)!;
        var temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, SettingsDocument.FromSettings(settings), SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _settingsPath, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new ApplicationSettingsPersistenceException("The settings file could not be saved.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ApplicationSettingsPersistenceException("The settings file could not be saved.", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed class SettingsDocument
    {
        public int SchemaVersion { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; }
        public string? PreferredUpsName { get; set; }
        public double PollingIntervalSeconds { get; set; }
        public double ConnectionTimeoutSeconds { get; set; }
        public ThemePreference Theme { get; set; }
        public bool MockMode { get; set; }

        public ApplicationSettings ToSettings() => new(
            SchemaVersion, Host!, Port, PreferredUpsName,
            TimeSpan.FromSeconds(PollingIntervalSeconds), TimeSpan.FromSeconds(ConnectionTimeoutSeconds), Theme, MockMode);

        public static SettingsDocument FromSettings(ApplicationSettings settings) => new()
        {
            SchemaVersion = settings.SchemaVersion,
            Host = settings.Host,
            Port = settings.Port,
            PreferredUpsName = settings.PreferredUpsName,
            PollingIntervalSeconds = settings.PollingInterval.TotalSeconds,
            ConnectionTimeoutSeconds = settings.ConnectionTimeout.TotalSeconds,
            Theme = settings.Theme,
            MockMode = settings.MockMode
        };
    }
}
