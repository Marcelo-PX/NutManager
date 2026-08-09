using NutManager.Infrastructure.Platform.Windows;
using Xunit;

namespace NutManager.Tests;

public sealed class WindowsNutInstallationDetectorTests
{
    private const string ProgramFiles = @"C:\Program Files";
    private const string ProgramFilesX86 = @"C:\Program Files (x86)";

    [Fact]
    public async Task ReturnsNotDetectedWhenNoKnownCandidateExists()
    {
        var fileSystem = new FakeFileSystem();
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.False(result.IsDetected);
        Assert.Null(result.InstallationDirectory);
        Assert.Empty(result.Executables);
        Assert.Empty(result.ConfigurationFiles);
        Assert.Equal(0, fileSystem.WriteAttempts);
        Assert.Equal(0, fileSystem.ProcessStartAttempts);
        Assert.Equal(0, fileSystem.AdminRequests);
    }

    [Fact]
    public async Task DetectsInstallationInProgramFilesAndReadsItsVersion()
    {
        var root = Path.Combine(ProgramFiles, "NUT");
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(Path.Combine(root, "bin", "upsd.exe"), version: "2.8.2");
        fileSystem.AddFile(Path.Combine(root, "etc", "ups.conf"));
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.True(result.IsDetected);
        Assert.Equal(root, result.InstallationDirectory);
        Assert.Equal(Path.Combine(root, "etc"), result.ConfigurationDirectory);
        Assert.Equal("2.8.2", result.Version);
        Assert.Equal("Program Files", result.DetectionSource);
        Assert.Equal(Path.Combine(root, "bin", "upsd.exe"), result.Executables["upsd.exe"]);
    }

    [Fact]
    public async Task DetectsAlternativeCNutInstallation()
    {
        var root = @"C:\NUT";
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(Path.Combine(root, "upsc.exe"));
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.True(result.IsDetected);
        Assert.Equal(root, result.InstallationDirectory);
        Assert.Equal(@"C:\NUT", result.DetectionSource);
        Assert.Equal("Indisponível", result.Version ?? "Indisponível");
    }

    [Fact]
    public async Task RecognizesEtcAsTheConfigurationDirectoryWithPartialFiles()
    {
        var root = @"C:\NUT";
        var configurationDirectory = Path.Combine(root, "etc");
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(Path.Combine(configurationDirectory, "ups.conf"));
        fileSystem.AddFile(Path.Combine(configurationDirectory, "upsd.users"));
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.True(result.IsDetected);
        Assert.Equal(configurationDirectory, result.ConfigurationDirectory);
        Assert.Equal(5, result.ConfigurationFiles.Count);
        Assert.Equal(2, result.ConfigurationFiles.Count(file => file.Exists));
        Assert.Contains(result.ConfigurationFiles, file => file.Name == "ups.conf" && file.IsReadable);
        Assert.Contains(result.ConfigurationFiles, file => file.Name == "nut.conf" && !file.Exists);
    }

    [Fact]
    public async Task RecognizesConfigurationFilesInTheInstallationRoot()
    {
        var root = Path.Combine(ProgramFilesX86, "Network UPS Tools");
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(Path.Combine(root, "nut.conf"));
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.True(result.IsDetected);
        Assert.Equal(root, result.InstallationDirectory);
        Assert.Equal(root, result.ConfigurationDirectory);
        Assert.Equal("Program Files (x86)", result.DetectionSource);
    }

    [Fact]
    public async Task DetectsKnownExecutablesIndividuallyAndHandlesUnavailableVersion()
    {
        var root = @"C:\NUT";
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(Path.Combine(root, "upsd.exe"));
        fileSystem.AddFile(Path.Combine(root, "bin", "upsdrvctl.exe"));
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.Equal(2, result.Executables.Count);
        Assert.Contains("upsd.exe", result.Executables.Keys);
        Assert.Contains("upsdrvctl.exe", result.Executables.Keys);
        Assert.DoesNotContain("upsc.exe", result.Executables.Keys);
        Assert.DoesNotContain("upsmon.exe", result.Executables.Keys);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task ManualInspectionAcceptsEitherInstallationOrConfigurationDirectory()
    {
        var root = @"D:\Applications\NUT";
        var configurationDirectory = Path.Combine(root, "config");
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(Path.Combine(root, "bin", "upsc.exe"));
        fileSystem.AddFile(Path.Combine(configurationDirectory, "upsmon.conf"));
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var fromRoot = await detector.InspectDirectoryAsync(root, CancellationToken.None);
        var fromConfiguration = await detector.InspectDirectoryAsync(configurationDirectory, CancellationToken.None);

        Assert.True(fromRoot.IsDetected);
        Assert.True(fromConfiguration.IsDetected);
        Assert.Equal(root, fromRoot.InstallationDirectory);
        Assert.Equal(root, fromConfiguration.InstallationDirectory);
        Assert.Equal(configurationDirectory, fromRoot.ConfigurationDirectory);
        Assert.Equal(configurationDirectory, fromConfiguration.ConfigurationDirectory);
        Assert.Equal("Diretório selecionado manualmente", fromConfiguration.DetectionSource);
    }

    [Fact]
    public async Task InvalidOrUnrecognizedManualDirectoryIsNotDetected()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddDirectory(@"D:\Empty");
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var missing = await detector.InspectDirectoryAsync(@"D:\Missing", CancellationToken.None);
        var empty = await detector.InspectDirectoryAsync(@"D:\Empty", CancellationToken.None);

        Assert.False(missing.IsDetected);
        Assert.Equal("O diretório selecionado não existe.", missing.ErrorMessage);
        Assert.False(empty.IsDetected);
        Assert.Null(empty.ErrorMessage);
    }

    [Fact]
    public async Task ReportsExistingConfigurationFilesThatCannotBeRead()
    {
        var root = @"C:\NUT";
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(Path.Combine(root, "etc", "upsd.conf"), readable: false);
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var result = await detector.DetectAsync(CancellationToken.None);

        var file = Assert.Single(result.ConfigurationFiles, file => file.Name == "upsd.conf");
        Assert.True(file.Exists);
        Assert.False(file.IsReadable);
    }

    [Fact]
    public async Task SelectsTheStrongestCandidateAndUsesKnownOrderForTies()
    {
        var programFilesRoot = Path.Combine(ProgramFiles, "NUT");
        var alternativeRoot = @"C:\NUT";
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(Path.Combine(programFilesRoot, "upsc.exe"));
        fileSystem.AddFile(Path.Combine(alternativeRoot, "upsd.exe"));
        fileSystem.AddFile(Path.Combine(alternativeRoot, "upsmon.exe"));
        var detector = new WindowsNutInstallationDetector(fileSystem);

        var strongest = await detector.DetectAsync(CancellationToken.None);
        Assert.Equal(alternativeRoot, strongest.InstallationDirectory);

        var tieFileSystem = new FakeFileSystem();
        tieFileSystem.AddFile(Path.Combine(programFilesRoot, "upsc.exe"));
        tieFileSystem.AddFile(Path.Combine(alternativeRoot, "upsc.exe"));
        var tieDetector = new WindowsNutInstallationDetector(tieFileSystem);

        var tie = await tieDetector.DetectAsync(CancellationToken.None);
        Assert.Equal(programFilesRoot, tie.InstallationDirectory);
    }

    [Fact]
    public async Task HonorsCancellationBeforeInspection()
    {
        var detector = new WindowsNutInstallationDetector(new FakeFileSystem());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => detector.DetectAsync(cancellation.Token));
    }

    private sealed class FakeFileSystem : IWindowsNutInstallationFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileEntry> _files = new(StringComparer.OrdinalIgnoreCase);

        public string ProgramFilesDirectory => ProgramFiles;
        public string ProgramFilesX86Directory => ProgramFilesX86;
        public int WriteAttempts { get; private set; }
        public int ProcessStartAttempts { get; private set; }
        public int AdminRequests { get; private set; }

        public void AddDirectory(string path)
        {
            var current = Normalize(path);
            while (!string.IsNullOrWhiteSpace(current))
            {
                _directories.Add(current);
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = Normalize(parent);
            }
        }

        public void AddFile(string path, bool readable = true, string? version = null)
        {
            AddDirectory(Path.GetDirectoryName(path)!);
            _files[Normalize(path)] = new FileEntry(readable, version);
        }

        public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

        public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

        public bool CanReadFile(string path) => _files.TryGetValue(Normalize(path), out var entry) && entry.Readable;

        public string? GetFileVersion(string path) => _files.TryGetValue(Normalize(path), out var entry) ? entry.Version : null;

        private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(path);

        private sealed record FileEntry(bool Readable, string? Version);
    }
}
