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
    public async Task MalformedJsonIsReportedAndPreserved()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(store.SettingsPath, "{ invalid");
        await Assert.ThrowsAsync<ApplicationSettingsPersistenceException>(() => store.LoadAsync(CancellationToken.None));
        Assert.Equal("{ invalid", await File.ReadAllTextAsync(store.SettingsPath));
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NutManager.Tests.{Guid.NewGuid():N}");
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
