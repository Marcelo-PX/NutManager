using NutManager.Core.Models;
using NutManager.Infrastructure.Platform.Windows;
using Xunit;

namespace NutManager.Tests;

public sealed class WindowsNutDriverCatalogSourceTests
{
    [Fact]
    public async Task PassivelyDiscoversDriverExecutablesInsideDetectedInstallationOnly()
    {
        if (!OperatingSystem.IsWindows()) return;
        var files = new FakeFileSystem(
            ["C:\\NUT\\bin\\nutdrv_qx.exe", "C:\\NUT\\bin\\vendor-driver.exe", "C:\\NUT\\bin\\upsdrvctl.exe"],
            ["C:\\Other\\external.exe"]);
        var source = new WindowsNutDriverCatalogSource(files);

        var names = await source.GetInstalledDriverNamesAsync(Installation(), CancellationToken.None);

        Assert.Contains("nutdrv_qx", names);
        Assert.Contains("vendor-driver", names);
        Assert.DoesNotContain("upsdrvctl", names);
        Assert.DoesNotContain("external", names);
        Assert.Empty(files.StartedProcesses);
    }

    [Fact]
    public async Task CancellationStopsPassiveEnumerationWithoutOpeningHardware()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new WindowsNutDriverCatalogSource(new FakeFileSystem([], []));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            source.GetInstalledDriverNamesAsync(Installation(), cancellation.Token));
    }

    private static NutInstallationInfo Installation() => new(
        true, "C:\\NUT", "C:\\NUT\\etc", "2.8.5",
        new Dictionary<string, string>(), Array.Empty<NutConfigurationFileInfo>(), "test");

    private sealed class FakeFileSystem : IWindowsNutDriverCatalogFileSystem
    {
        private readonly IReadOnlyList<string> _inside;
        private readonly IReadOnlyList<string> _outside;

        public FakeFileSystem(IReadOnlyList<string> inside, IReadOnlyList<string> outside)
        {
            _inside = inside;
            _outside = outside;
        }

        public List<string> StartedProcesses { get; } = [];
        public bool FileExists(string path) => _inside.Contains(path, StringComparer.OrdinalIgnoreCase);
        public IEnumerable<string> EnumerateFiles(string directory, string pattern) =>
            _inside.Concat(_outside).Where(path => path.StartsWith(directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }
}
