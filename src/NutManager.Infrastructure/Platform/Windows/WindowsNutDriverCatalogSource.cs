using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Platform.Windows;

public interface IWindowsNutDriverCatalogFileSystem
{
    bool FileExists(string path);
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
}

public sealed class WindowsNutDriverCatalogSource : ILocalNutDriverCatalogSource
{
    private readonly IWindowsNutDriverCatalogFileSystem _fileSystem;

    public WindowsNutDriverCatalogSource()
        : this(new WindowsNutDriverCatalogFileSystem())
    {
    }

    public WindowsNutDriverCatalogSource(IWindowsNutDriverCatalogFileSystem fileSystem) =>
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(NutInstallationInfo installation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !installation.IsDetected ||
            !WindowsPath.TryCanonicalize(installation.InstallationDirectory, out var install))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in NutUpsConfigurationCatalog.CreateDriverSchemas())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executable = schema.DriverId + ".exe";
            var candidates = new[] { $"{install.TrimEnd('\\')}\\{executable}", $"{install.TrimEnd('\\')}\\bin\\{executable}" };
            if (candidates.Any(candidate => WindowsPath.IsInside(candidate, install) && _fileSystem.FileExists(candidate)))
                names.Add(schema.DriverId);
        }

        foreach (var directory in new[] { install, $"{install.TrimEnd('\\')}\\bin" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in _fileSystem.EnumerateFiles(directory, "*.exe"))
            {
                if (!WindowsPath.TryCanonicalize(path, out var canonical) || !WindowsPath.IsInside(canonical, install)) continue;
                var separator = canonical.LastIndexOf('\\');
                var name = separator >= 0 ? canonical[(separator + 1)..] : canonical;
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                var driverName = name[..^4];
                if (IsNutTool(driverName) || !NutDriverCatalog.IsValidDriverName(driverName)) continue;
                names.Add(driverName);
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool IsNutTool(string name) => name.ToLowerInvariant() is
        "nut" or "upsd" or "upsmon" or "upsdrvctl" or "upsc" or "upscmd" or "upsrw" or "upslog" or "nut-scanner";
}

public sealed class WindowsNutDriverCatalogFileSystem : IWindowsNutDriverCatalogFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
    {
        try { return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToArray(); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
