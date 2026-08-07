using NutManager.Core.Models;
using NutManager.Infrastructure.Persistence;
using Xunit;

namespace NutManager.Tests;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public void DefaultsAreSafeAndUseMockMode()
    {
        var settings = new ApplicationSettings();
        Assert.Equal("localhost", settings.Host);
        Assert.Equal(3493, settings.Port);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.PollingInterval);
        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.True(settings.MockMode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void InvalidPortsAreRejected(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(port: port));

    [Fact]
    public void InvalidHostAndIntervalsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ApplicationSettings(host: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(pollingInterval: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(connectionTimeout: TimeSpan.Zero));
    }

    [Fact]
    public void InvalidSchemaThemeAndNegativeIntervalsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(schemaVersion: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(theme: (ThemePreference)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(pollingInterval: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(connectionTimeout: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void WhitespacePreferredUpsIsNormalizedToNull() =>
        Assert.Null(new ApplicationSettings(preferredUpsName: "  ").PreferredUpsName);

    [Fact]
    public async Task StoreReturnsDefaultsWithoutCreatingAFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        var settings = await store.LoadAsync(CancellationToken.None);
        Assert.Equal("localhost", settings.Host);
        Assert.False(File.Exists(store.SettingsPath));
    }

    [Fact]
    public async Task StoreRoundTripsReadableJsonAndSettings()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        var expected = new ApplicationSettings(host: "nut.local", port: 1234, preferredUpsName: "ups-a",
            pollingInterval: TimeSpan.FromSeconds(9), connectionTimeout: TimeSpan.FromSeconds(3), theme: ThemePreference.Dark, mockMode: false);
        await store.SaveAsync(expected, CancellationToken.None);
        var json = await File.ReadAllTextAsync(store.SettingsPath);
        var actual = await store.LoadAsync(CancellationToken.None);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"theme\": \"Dark\"", json);
        Assert.Equal(expected, actual);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task StoreRoundTripsNullPreferredUpsAndCreatesItsDirectory()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "missing");
        var store = new JsonApplicationSettingsStore(root);
        await store.SaveAsync(new ApplicationSettings(preferredUpsName: null, mockMode: true), CancellationToken.None);

        var settings = await store.LoadAsync(CancellationToken.None);
        Assert.True(Directory.Exists(root));
        Assert.Null(settings.PreferredUpsName);
        Assert.True(settings.MockMode);
    }

    [Fact]
    public async Task SecondSaveReplacesThePreviousSettings()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        await store.SaveAsync(new ApplicationSettings(host: "first"), CancellationToken.None);
        await store.SaveAsync(new ApplicationSettings(host: "second"), CancellationToken.None);

        Assert.Equal("second", (await store.LoadAsync(CancellationToken.None)).Host);
    }

    [Fact]
    public async Task MalformedJsonIsReportedAndPreserved()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(store.SettingsPath, "{ invalid");
        await Assert.ThrowsAsync<ApplicationSettingsPersistenceException>(() => store.LoadAsync(CancellationToken.None));
        Assert.Equal("{ invalid", await File.ReadAllTextAsync(store.SettingsPath));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"host\":\"localhost\",\"port\":3493,\"pollingIntervalSeconds\":5,\"connectionTimeoutSeconds\":5,\"theme\":\"System\",\"mockMode\":true}")]
    [InlineData("{\"schemaVersion\":1,\"host\":\" \",\"port\":3493,\"pollingIntervalSeconds\":5,\"connectionTimeoutSeconds\":5,\"theme\":\"System\",\"mockMode\":true}")]
    public async Task UnsupportedOrInvalidJsonIsReportedAndPreserved(string json)
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(store.SettingsPath, json);
        await Assert.ThrowsAsync<ApplicationSettingsPersistenceException>(() => store.LoadAsync(CancellationToken.None));
        Assert.Equal(json, await File.ReadAllTextAsync(store.SettingsPath));
    }

    [Fact]
    public async Task CancelledOperationsDoNotWriteAFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new ApplicationSettings(), cancellation.Token));
        Assert.False(File.Exists(store.SettingsPath));
    }

    [Fact]
    public async Task CancelledSavePreservesAnExistingFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        await store.SaveAsync(new ApplicationSettings(host: "original"), CancellationToken.None);
        var original = await File.ReadAllTextAsync(store.SettingsPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new ApplicationSettings(host: "new"), cancellation.Token));
        Assert.Equal(original, await File.ReadAllTextAsync(store.SettingsPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NutManager.Tests.{Guid.NewGuid():N}");
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
