namespace NutManager.Infrastructure.Remote.Smb;

/// <summary>
/// UNC file access used by the SMB transport. Directory metadata APIs do not expose a
/// cancellable asynchronous equivalent; cancellation is checked before and after them.
/// All mutable operations remain direct and are never abandoned on a background task.
/// </summary>
public sealed class WindowsSmbFileSystem : ISmbFileSystem
{
    private const int BufferSize = 81920;
    private const long MaximumFileSize = 8 * 1024 * 1024;

    public Task<IReadOnlyList<SmbFileSystemEntry>> ListDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = Directory.EnumerateFileSystemEntries(directory)
            .Select(path =>
            {
                var attributes = File.GetAttributes(path);
                return new SmbFileSystemEntry(
                    Path.GetFileName(path),
                    path,
                    (attributes & FileAttributes.Directory) != 0,
                    (attributes & FileAttributes.ReparsePoint) != 0);
            })
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SmbFileSystemEntry>>(entries);
    }

    public async Task<ReadOnlyMemory<byte>> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumFileSize)
        {
            throw new IOException("The SMB configuration file exceeds the supported size.");
        }

        using var memory = new MemoryStream(stream.Length is > 0 and <= int.MaxValue ? (int)stream.Length : 0);
        await stream.CopyToAsync(memory, BufferSize, cancellationToken);
        return memory.ToArray();
    }

    public async Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path));
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        return Task.CompletedTask;
    }

    public Task ReplaceFileAsync(string candidatePath, string targetPath, string backupPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Replace(candidatePath, targetPath, backupPath, ignoreMetadataErrors: false);
        return Task.CompletedTask;
    }

    public Task<bool> IsReparsePointAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attributes = File.GetAttributes(path);
        return Task.FromResult((attributes & FileAttributes.ReparsePoint) != 0);
    }
}
