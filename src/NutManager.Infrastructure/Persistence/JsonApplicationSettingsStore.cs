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
        _ = new ApplicationSettings(
            settings.SchemaVersion,
            settings.PollingInterval,
            settings.ConnectionTimeout,
            settings.Theme,
            settings.MockMode,
            settings.Language,
            settings.SidebarPreference);

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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Host { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Port { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PreferredUpsName { get; set; }
        public double PollingIntervalSeconds { get; set; }
        public double ConnectionTimeoutSeconds { get; set; }
        public ThemePreference Theme { get; set; }
        public bool? MockMode { get; set; }
        public UiLanguagePreference Language { get; set; } = UiLanguagePreference.PtBr;
        public SidebarPreference SidebarPreference { get; set; } = SidebarPreference.Expanded;

        public ApplicationSettings ToSettings()
        {
            if (SchemaVersion is < 1 or > ApplicationSettings.CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(SchemaVersion), "Unsupported settings schema version.");
            }

            if (MockMode is null)
            {
                throw new ArgumentException("The mock-mode preference is required.", nameof(MockMode));
            }

            var legacyEndpoint = SchemaVersion <= 2
                ? new LegacyMonitoringEndpoint(Host!, Port ?? 0, PreferredUpsName)
                : null;
            return new ApplicationSettings(
                ApplicationSettings.CurrentSchemaVersion,
                TimeSpan.FromSeconds(PollingIntervalSeconds),
                TimeSpan.FromSeconds(ConnectionTimeoutSeconds),
                Theme,
                MockMode.Value,
                SchemaVersion == 1 ? UiLanguagePreference.PtBr : Language,
                SchemaVersion == 1 ? SidebarPreference.Expanded : SidebarPreference,
                legacyEndpoint);
        }

        public static SettingsDocument FromSettings(ApplicationSettings settings) => new()
        {
            SchemaVersion = settings.SchemaVersion,
            PollingIntervalSeconds = settings.PollingInterval.TotalSeconds,
            ConnectionTimeoutSeconds = settings.ConnectionTimeout.TotalSeconds,
            Theme = settings.Theme,
            MockMode = settings.MockMode,
            Language = settings.Language,
            SidebarPreference = settings.SidebarPreference
        };
    }
}
