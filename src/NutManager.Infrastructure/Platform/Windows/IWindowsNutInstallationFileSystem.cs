namespace NutManager.Infrastructure.Platform.Windows;

public interface IWindowsNutInstallationFileSystem
{
    string ProgramFilesDirectory { get; }

    string ProgramFilesX86Directory { get; }

    bool DirectoryExists(string path);

    bool FileExists(string path);

    bool CanReadFile(string path);

    string? GetFileVersion(string path);
}
