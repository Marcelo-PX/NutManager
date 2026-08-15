using NutManager.Core.Models;
using NutManager.Infrastructure.Persistence;
using Xunit;

namespace NutManager.Tests;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public void NewInstallationDefaultsArePreferencesOnlyAndDisableMockMode()
    {
        var settings = new ApplicationSettings();

        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.ConnectionTimeout);
        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.False(settings.MockMode);
        Assert.Equal(UiLanguagePreference.PtBr, settings.Language);
        Assert.Equal(SidebarPreference.Expanded, settings.SidebarPreference);
        Assert.Null(settings.LegacyMonitoringEndpoint);
    }

    [Fact]
    public void InvalidSchemaEnumsAndIntervalsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(schemaVersion: 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(theme: (ThemePreference)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(language: (UiLanguagePreference)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(sidebarPreference: (SidebarPreference)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(pollingInterval: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(connectionTimeout: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task MissingSettingsReturnNewInstallDefaultsWithoutCreatingAFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.False(settings.MockMode);
        Assert.Null(settings.LegacyMonitoringEndpoint);
        Assert.False(File.Exists(store.SettingsPath));
    }

    [Fact]
    public async Task CurrentSettingsRoundTripPreferencesWithoutEndpointMirror()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        var expected = new ApplicationSettings(
            pollingInterval: TimeSpan.FromSeconds(9),
            connectionTimeout: TimeSpan.FromSeconds(3),
            theme: ThemePreference.Dark,
            mockMode: true,
            language: UiLanguagePreference.EnUs,
            sidebarPreference: SidebarPreference.Collapsed,
            backgroundTransparency: false);

        await store.SaveAsync(expected, CancellationToken.None);
        var json = await File.ReadAllTextAsync(store.SettingsPath);
        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.Contains("\"schemaVersion\": 5", json);
        // The value that is not the default has to survive the round trip, or switching the
        // backdrop off would silently come back on at the next start.
        Assert.False(actual.BackgroundTransparency);
        Assert.DoesNotContain("\"host\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"port\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preferredUps", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, actual);
        Assert.Null(actual.LegacyMonitoringEndpoint);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public async Task LegacySettingsExposeEndpointOnlyForBootstrapAndPreserveMockChoice(int schemaVersion, bool mockMode)
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        var visual = schemaVersion == 1 ? string.Empty : ",\"language\":\"EnUs\",\"sidebarPreference\":\"Collapsed\"";
        var json = $"{{\"schemaVersion\":{schemaVersion},\"host\":\"nut.local\",\"port\":1234,\"preferredUpsName\":\"ups-a\",\"pollingIntervalSeconds\":9,\"connectionTimeoutSeconds\":3,\"theme\":\"Dark\",\"mockMode\":{mockMode.ToString().ToLowerInvariant()}{visual}}}";
        await File.WriteAllTextAsync(store.SettingsPath, json);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal("nut.local", settings.LegacyMonitoringEndpoint!.Host);
        Assert.Equal(1234, settings.LegacyMonitoringEndpoint.Port);
        Assert.Equal("ups-a", settings.LegacyMonitoringEndpoint.PreferredUpsName);
        Assert.Equal(mockMode, settings.MockMode);
        Assert.Equal(schemaVersion == 1 ? UiLanguagePreference.PtBr : UiLanguagePreference.EnUs, settings.Language);
        Assert.Equal(schemaVersion == 1 ? SidebarPreference.Expanded : SidebarPreference.Collapsed, settings.SidebarPreference);
        Assert.Equal(json, await File.ReadAllTextAsync(store.SettingsPath));
    }

    [Fact]
    public async Task SavingMigratedSettingsDropsLegacyEndpointCompatibilityFields()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(store.SettingsPath, "{\"schemaVersion\":2,\"host\":\"legacy\",\"port\":3493,\"pollingIntervalSeconds\":5,\"connectionTimeoutSeconds\":5,\"theme\":\"System\",\"mockMode\":false,\"language\":\"PtBr\",\"sidebarPreference\":\"Expanded\"}");
        var migrated = await store.LoadAsync(CancellationToken.None);

        await store.SaveAsync(migrated, CancellationToken.None);
        var json = await File.ReadAllTextAsync(store.SettingsPath);

        Assert.DoesNotContain("legacy", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"host\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.False((await store.LoadAsync(CancellationToken.None)).MockMode);
    }

    [Fact]
    public async Task SettingsWrittenBeforeTheTransparencyPreferenceExistedOpenWithItOn()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(
            store.SettingsPath,
            """
            {"schemaVersion":4,"pollingIntervalSeconds":5,"connectionTimeoutSeconds":5,
             "theme":"System","mockMode":false,"language":"PtBr","sidebarPreference":"Expanded"}
            """);

        var loaded = await store.LoadAsync(CancellationToken.None);

        // The backdrop was always on before the switch existed, so a file that predates it must not
        // open with the effect silently turned off.
        Assert.True(loaded.BackgroundTransparency);
        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Theory]
    [InlineData("{ invalid")]
    [InlineData("{\"schemaVersion\":6,\"pollingIntervalSeconds\":5,\"connectionTimeoutSeconds\":5,\"theme\":\"System\",\"mockMode\":false,\"language\":\"PtBr\",\"sidebarPreference\":\"Expanded\"}")]
    [InlineData("{\"schemaVersion\":2,\"host\":\"localhost\",\"port\":3493,\"pollingIntervalSeconds\":5,\"connectionTimeoutSeconds\":5,\"theme\":\"System\",\"language\":\"PtBr\",\"sidebarPreference\":\"Expanded\"}")]
    [InlineData("{\"schemaVersion\":2,\"host\":\" \",\"port\":3493,\"pollingIntervalSeconds\":5,\"connectionTimeoutSeconds\":5,\"theme\":\"System\",\"mockMode\":true,\"language\":\"PtBr\",\"sidebarPreference\":\"Expanded\"}")]
    public async Task MalformedOrIncompatibleSettingsAreReportedAndPreserved(string json)
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(store.SettingsPath, json);

        await Assert.ThrowsAsync<ApplicationSettingsPersistenceException>(() => store.LoadAsync(CancellationToken.None));

        Assert.Equal(json, await File.ReadAllTextAsync(store.SettingsPath));
    }

    [Fact]
    public async Task CancelledOperationsDoNotCreateOrReplaceSettings()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        await store.SaveAsync(new ApplicationSettings(theme: ThemePreference.Dark), CancellationToken.None);
        var original = await File.ReadAllTextAsync(store.SettingsPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new ApplicationSettings(theme: ThemePreference.Light), cancellation.Token));

        Assert.Equal(original, await File.ReadAllTextAsync(store.SettingsPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NutManager.Tests.{Guid.NewGuid():N}");

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
