using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Platform.Windows;
using Xunit;

namespace NutManager.Tests;

public sealed class WindowsNutVersionResolverTests
{
    [Fact]
    public async Task FileMetadataRemainsThePrimaryVersionSource()
    {
        var runner = new FakeRunner(new(true, "Network UPS Tools 9.9.9"));
        var resolver = new WindowsNutVersionResolver(runner);

        var result = await resolver.ResolveAsync(CreateInstallation("2.8.5"), CancellationToken.None);

        Assert.Equal("2.8.5", result.Version);
        Assert.Equal(NutVersionSource.FileMetadata, result.Source);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task MissingMetadataUsesOneBoundedReadOnlyVersionProbe()
    {
        var runner = new FakeRunner(new(true, "Network UPS Tools upsdrvctl 2.8.5\n"));
        var resolver = new WindowsNutVersionResolver(runner);

        var result = await resolver.ResolveAsync(CreateInstallation(null), CancellationToken.None);

        Assert.Equal("2.8.5", result.Version);
        Assert.Equal(NutVersionSource.ExecutableFallback, result.Source);
        Assert.Equal(1, runner.Calls);
        Assert.Equal(@"C:\NUT\bin\upsdrvctl.exe", runner.ExecutablePath);
    }

    [Theory]
    [InlineData(false, "Network UPS Tools 2.8.5")]
    [InlineData(true, "unexpected output")]
    [InlineData(true, "")]
    public async Task TimeoutOrMalformedOutputRemainsUnavailable(bool completed, string output)
    {
        var resolver = new WindowsNutVersionResolver(new FakeRunner(new(completed, output)));

        var result = await resolver.ResolveAsync(CreateInstallation(null), CancellationToken.None);

        Assert.Null(result.Version);
        Assert.Equal(NutVersionSource.Unavailable, result.Source);
    }

    [Fact]
    public async Task MissingOrExternalExecutableNeverLaunches()
    {
        var runner = new FakeRunner(new(true, "2.8.5"));
        var resolver = new WindowsNutVersionResolver(runner);
        var installation = CreateInstallation(null) with
        {
            Executables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["upsdrvctl.exe"] = @"C:\Other\upsdrvctl.exe"
            }
        };

        var result = await resolver.ResolveAsync(installation, CancellationToken.None);

        Assert.Equal(NutVersionSource.Unavailable, result.Source);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task CallerCancellationIsPropagatedBeforeAnyLaunch()
    {
        var runner = new FakeRunner(new(true, "2.8.5"));
        var resolver = new WindowsNutVersionResolver(runner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveAsync(CreateInstallation(null), cancellation.Token));
        Assert.Equal(0, runner.Calls);
    }

    private static NutInstallationInfo CreateInstallation(string? version) => new(
        true,
        @"C:\NUT",
        @"C:\NUT\etc",
        version,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["upsdrvctl.exe"] = @"C:\NUT\bin\upsdrvctl.exe"
        },
        Array.Empty<NutConfigurationFileInfo>(),
        "test");

    private sealed class FakeRunner(WindowsNutVersionProcessResult result) : IWindowsNutVersionProcessRunner
    {
        public int Calls { get; private set; }
        public string? ExecutablePath { get; private set; }
        public Task<WindowsNutVersionProcessResult> RunVersionAsync(string executablePath, CancellationToken cancellationToken)
        {
            Calls++;
            ExecutablePath = executablePath;
            return Task.FromResult(result);
        }
    }
}
