using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Platform.Windows;

public sealed class WindowsNutInstallationDetector : ILocalNutInstallationDetector
{
    private static readonly string[] KnownExecutableNames = ["upsd.exe", "upsc.exe", "upsdrvctl.exe", "upsmon.exe"];
    private static readonly string[] KnownConfigurationFileNames = ["nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf"];
    private static readonly string[] ConfigurationDirectoryNames = ["etc", "config", "conf"];

    private readonly IWindowsNutInstallationFileSystem _fileSystem;

    public WindowsNutInstallationDetector()
        : this(new WindowsNutInstallationFileSystem())
    {
    }

    public WindowsNutInstallationDetector(IWindowsNutInstallationFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    public Task<NutInstallationInfo> DetectAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = GetKnownCandidates();
        var inspected = candidates
            .Select((candidate, index) => (Candidate: candidate, Index: index, Inspection: Inspect(candidate.Path, candidate.Source, cancellationToken)))
            .Where(result => result.Inspection.Info.IsDetected)
            .OrderByDescending(result => result.Inspection.Score)
            .ThenBy(result => result.Index)
            .FirstOrDefault();

        return inspected.Inspection?.Info ?? NutInstallationInfo.NotDetected();
    }, cancellationToken);

    public Task<NutInstallationInfo> InspectDirectoryAsync(string installationOrConfigurationDirectory, CancellationToken cancellationToken) => Task.Run(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationOrConfigurationDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        return Inspect(installationOrConfigurationDirectory, "Diretório selecionado manualmente", cancellationToken).Info;
    }, cancellationToken);

    private IEnumerable<(string Path, string Source)> GetKnownCandidates()
    {
        var candidates = new List<(string Path, string Source)>();
        AddProgramFilesCandidates(candidates, _fileSystem.ProgramFilesDirectory, "Program Files");
        AddProgramFilesCandidates(candidates, _fileSystem.ProgramFilesX86Directory, "Program Files (x86)");
        candidates.Add((@"C:\NUT", @"C:\NUT"));

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Path))
            .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddProgramFilesCandidates(ICollection<(string Path, string Source)> candidates, string directory, string source)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        candidates.Add((Path.Combine(directory, "NUT"), source));
        candidates.Add((Path.Combine(directory, "Network UPS Tools"), source));
    }

    private Inspection Inspect(string selectedDirectory, string source, CancellationToken cancellationToken)
    {
        var directory = Path.TrimEndingDirectorySeparator(selectedDirectory);
        if (!_fileSystem.DirectoryExists(directory))
        {
            return new Inspection(NutInstallationInfo.NotDetected(source, "O diretório selecionado não existe."), 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var installationDirectory = IsConfigurationDirectory(directory)
            ? Directory.GetParent(directory)?.FullName ?? directory
            : directory;
        var executables = InspectExecutables(installationDirectory, cancellationToken);
        var configuration = FindBestConfigurationDirectory(installationDirectory, directory, cancellationToken);
        var score = executables.Count + configuration.Files.Count(file => file.Exists);

        if (score == 0)
        {
            return new Inspection(NutInstallationInfo.NotDetected(source), 0);
        }

        var version = GetVersion(executables);
        return new Inspection(
            new NutInstallationInfo(
                true,
                installationDirectory,
                configuration.Directory,
                version,
                executables,
                configuration.Files,
                source),
            score);
    }

    private Dictionary<string, string> InspectExecutables(string installationDirectory, CancellationToken cancellationToken)
    {
        var locations = new[] { installationDirectory, Path.Combine(installationDirectory, "bin") };
        var executables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var executableName in KnownExecutableNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executablePath = locations
                .Select(location => Path.Combine(location, executableName))
                .FirstOrDefault(_fileSystem.FileExists);
            if (executablePath is not null)
            {
                executables.Add(executableName, executablePath);
            }
        }

        return executables;
    }

    private ConfigurationInspection FindBestConfigurationDirectory(
        string installationDirectory,
        string selectedDirectory,
        CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        if (IsConfigurationDirectory(selectedDirectory))
        {
            directories.Add(selectedDirectory);
        }

        directories.AddRange(ConfigurationDirectoryNames.Select(name => Path.Combine(installationDirectory, name)));
        directories.Add(installationDirectory);

        return directories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((directory, index) => InspectConfigurationDirectory(directory, index, cancellationToken))
            .Where(inspection => inspection.Files.Count > 0)
            .OrderByDescending(inspection => inspection.Files.Count(file => file.Exists))
            .ThenBy(inspection => inspection.Index)
            .FirstOrDefault()
            ?? new ConfigurationInspection(null, Array.Empty<NutConfigurationFileInfo>(), int.MaxValue);
    }

    private ConfigurationInspection InspectConfigurationDirectory(string directory, int index, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_fileSystem.DirectoryExists(directory))
        {
            return new ConfigurationInspection(null, Array.Empty<NutConfigurationFileInfo>(), index);
        }

        var files = KnownConfigurationFileNames
            .Select(fileName => InspectConfigurationFile(directory, fileName, cancellationToken))
            .ToArray();
        return files.Any(file => file.Exists)
            ? new ConfigurationInspection(directory, files, index)
            : new ConfigurationInspection(null, Array.Empty<NutConfigurationFileInfo>(), index);
    }

    private NutConfigurationFileInfo InspectConfigurationFile(string directory, string fileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(directory, fileName);
        var exists = _fileSystem.FileExists(path);
        return new NutConfigurationFileInfo(fileName, path, exists, exists && _fileSystem.CanReadFile(path));
    }

    private string? GetVersion(IReadOnlyDictionary<string, string> executables)
    {
        foreach (var executableName in KnownExecutableNames)
        {
            if (executables.TryGetValue(executableName, out var path))
            {
                var version = _fileSystem.GetFileVersion(path);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }
        }

        return null;
    }

    private static bool IsConfigurationDirectory(string directory) =>
        ConfigurationDirectoryNames.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)), StringComparer.OrdinalIgnoreCase);

    private sealed record Inspection(NutInstallationInfo Info, int Score);

    private sealed record ConfigurationInspection(string? Directory, IReadOnlyList<NutConfigurationFileInfo> Files, int Index);
}
