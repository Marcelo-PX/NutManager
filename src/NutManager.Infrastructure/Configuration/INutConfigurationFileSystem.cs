namespace NutManager.Infrastructure.Configuration;

public interface INutConfigurationFileSystem
{
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken);

    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);

    Task ReplaceAsync(string sourcePath, string destinationPath, string? backupPath, CancellationToken cancellationToken);

    Task DeleteFileIfExistsAsync(string path, CancellationToken cancellationToken);
}
