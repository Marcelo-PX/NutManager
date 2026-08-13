using NutManager.Core.Administration;
using NutManager.Infrastructure.Platform.Windows;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The Windows NUT service registers <c>nut.exe</c> as its executable. These tests pin that the
/// installation detector recognises it and that the service association trusts it only inside the
/// detected installation root. Everything runs against fakes: no real SCM, filesystem or service.
/// </summary>
public sealed class WindowsNutServiceHostExecutableTests
{
    private const string Root = @"C:\NUT";

    [Fact]
    public async Task ServiceHostExecutableIsDiscoveredInTheInstallationBinFolder()
    {
        // Regression: only upsc.exe was reported, so the binary the service actually runs was
        // invisible in diagnostics even though it sat next to it.
        var fileSystem = new FakeInstallationFileSystem();
        fileSystem.AddFile(@"C:\NUT\bin\nut.exe");
        fileSystem.AddFile(@"C:\NUT\bin\upsc.exe");
        fileSystem.AddFile(@"C:\NUT\etc\ups.conf");
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.True(result.IsDetected);
        Assert.Equal(Root, result.InstallationDirectory);
        Assert.Contains("nut.exe", result.Executables.Keys);
        Assert.Equal(@"C:\NUT\bin\nut.exe", result.Executables["nut.exe"]);
    }

    [Fact]
    public async Task ExistingExecutablesKeepBeingDiscoveredAlongsideTheServiceHost()
    {
        var fileSystem = new FakeInstallationFileSystem();
        fileSystem.AddFile(@"C:\NUT\bin\nut.exe");
        fileSystem.AddFile(@"C:\NUT\bin\upsc.exe");
        fileSystem.AddFile(@"C:\NUT\bin\upsd.exe", "2.8.5");
        fileSystem.AddFile(@"C:\NUT\etc\ups.conf");
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.Contains("upsc.exe", result.Executables.Keys);
        Assert.Contains("upsd.exe", result.Executables.Keys);
        Assert.Contains("nut.exe", result.Executables.Keys);
        // Version precedence is unchanged: the daemon is still consulted before the service host.
        Assert.Equal("2.8.5", result.Version);
    }

    [Fact]
    public async Task UnrelatedExecutableIsNotClassifiedAsTheServiceHost()
    {
        var fileSystem = new FakeInstallationFileSystem();
        fileSystem.AddFile(@"C:\NUT\bin\nutty.exe");
        fileSystem.AddFile(@"C:\NUT\bin\upsc.exe");
        fileSystem.AddFile(@"C:\NUT\etc\ups.conf");
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.DoesNotContain("nut.exe", result.Executables.Keys);
        Assert.DoesNotContain("nutty.exe", result.Executables.Keys);
    }

    [Theory]
    [InlineData(@"C:\NUT\bin\nut.exe")]
    [InlineData(@"""C:\NUT\bin\nut.exe""")]
    [InlineData(@"""C:\NUT\bin\nut.exe"" --service")]
    [InlineData(@"C:\NUT\bin\nut.exe --service")]
    public void RealServiceIsTrustedForEverySupportedPathNameForm(string pathName)
    {
        var (binaryPath, confidence) = WindowsNutServiceAssociation.Determine(
            "Network UPS Tools", "Network UPS Tools", pathName, Root);

        Assert.Equal(@"C:\NUT\bin\nut.exe", binaryPath);
        Assert.Equal(NutAssociationConfidence.BinaryPath, confidence);
    }

    [Theory]
    // Name alone is never proof: each of these sits outside the trusted installation root.
    [InlineData(@"C:\NUT-malicious\nut.exe")]
    [InlineData(@"C:\NUT2\bin\nut.exe")]
    [InlineData(@"C:\Temp\nut.exe")]
    [InlineData(@"""C:\Users\Public\nut.exe"" --service")]
    public void HostileExecutablePathsAreRejectedEvenWithTheKnownServiceName(string pathName)
    {
        var (_, confidence) = WindowsNutServiceAssociation.Determine(
            "Network UPS Tools", "Network UPS Tools", pathName, Root);

        Assert.Equal(NutAssociationConfidence.None, confidence);
    }

    [Fact]
    public void RunningServiceReportsItsStateAndDisablesStart()
    {
        var service = new NutServiceInfo("Network UPS Tools", "Network UPS Tools", NutServiceState.Running,
            NutServiceStartMode.Automatic, @"C:\NUT\bin\nut.exe", NutAssociationConfidence.BinaryPath);

        var controllable = service.AssociationConfidence == NutAssociationConfidence.BinaryPath;

        Assert.True(service.IsAssociated);
        Assert.Equal(NutServiceState.Running, service.State);
        Assert.Equal(NutServiceStartMode.Automatic, service.StartMode);
        // Running: start is off, the other two are available to the T16 workflow.
        Assert.False(controllable && service.State == NutServiceState.Stopped);
        Assert.True(controllable && service.State == NutServiceState.Running);
    }

    [Fact]
    public void WithoutAnAssociatedServiceNoAdministrativeCommandIsOffered()
    {
        NutServiceInfo? none = null;
        var controllable = none?.AssociationConfidence == NutAssociationConfidence.BinaryPath;

        Assert.False(controllable);
    }

    /// <summary>Deterministic in-memory filesystem; never touches the real disk.</summary>
    private sealed class FakeInstallationFileSystem : IWindowsNutInstallationFileSystem
    {
        private readonly Dictionary<string, string?> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public string ProgramFilesDirectory => @"C:\Program Files";
        public string ProgramFilesX86Directory => @"C:\Program Files (x86)";

        public void AddFile(string path, string? version = null)
        {
            _files[path] = version;
            var directory = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(directory))
            {
                _directories.Add(directory);
                directory = Path.GetDirectoryName(directory);
            }
        }

        public bool DirectoryExists(string path) => _directories.Contains(Path.TrimEndingDirectorySeparator(path));
        public bool FileExists(string path) => _files.ContainsKey(path);
        public bool CanReadFile(string path) => _files.ContainsKey(path);
        public string? GetFileVersion(string path) => _files.TryGetValue(path, out var version) ? version : null;
        public string? GetParentDirectory(string path) => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
    }
}
