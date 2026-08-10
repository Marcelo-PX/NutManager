namespace NutManager.Infrastructure.Remote.Smb;

/// <summary>
/// Narrow SMB filesystem seam. Production uses UNC paths through System.IO; tests use
/// deterministic fakes without a real share or domain connection.
/// </summary>
public interface ISmbFileSystem
{
    Task<IReadOnlyList<SmbFileSystemEntry>> ListDirectoryAsync(string directory, CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> ReadFileAsync(string path, CancellationToken cancellationToken);

    Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    Task ReplaceFileAsync(string candidatePath, string targetPath, string backupPath, CancellationToken cancellationToken);

    Task<bool> IsReparsePointAsync(string path, CancellationToken cancellationToken);
}

public sealed record SmbFileSystemEntry(string Name, string FullPath, bool IsDirectory, bool IsReparsePoint);
