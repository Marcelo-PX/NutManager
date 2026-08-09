using System.Diagnostics;

namespace NutManager.Infrastructure.Platform.Windows;

public sealed class WindowsNutInstallationFileSystem : IWindowsNutInstallationFileSystem
{
    public string ProgramFilesDirectory => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public string ProgramFilesX86Directory => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool CanReadFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? GetFileVersion(string path)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(version.ProductVersion) ? version.FileVersion : version.ProductVersion;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
