namespace NutManager.Infrastructure.Remote.Smb;

/// <summary>
/// UNC file access used by the SMB transport. Synchronous System.IO metadata and replace
/// APIs are dispatched off the UI thread. They have no safe cancellation once dispatched:
/// callers may cancel before dispatch, but mutable work is always awaited to completion.
/// </summary>
public sealed class WindowsSmbFileSystem : ISmbFileSystem
{
    private const int BufferSize = 81920;
    private const long MaximumFileSize = 8 * 1024 * 1024;

    public Task<IReadOnlyList<SmbFileSystemEntry>> ListDirectoryAsync(string directory, CancellationToken cancellationToken) =>
        ExecuteSynchronousAsync(() =>
        {
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
            return (IReadOnlyList<SmbFileSystemEntry>)entries;
        }, cancellationToken);

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

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken) =>
        ExecuteSynchronousAsync(() => File.Exists(path), cancellationToken);

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken) =>
        ExecuteSynchronousAsync(() => File.Delete(path), cancellationToken);

    public Task ReplaceFileAsync(string candidatePath, string targetPath, string backupPath, CancellationToken cancellationToken) =>
        ExecuteSynchronousAsync(() => File.Replace(candidatePath, targetPath, backupPath, ignoreMetadataErrors: false), cancellationToken);

    public Task<bool> IsReparsePointAsync(string path, CancellationToken cancellationToken) =>
        ExecuteSynchronousAsync(() => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0, cancellationToken);

    private static async Task<T> ExecuteSynchronousAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Task.Run is the dispatch boundary, not a timeout/cancellation wrapper. Once the
        // worker owns a synchronous SMB call it is awaited completely by the session gate.
        return await Task.Run(operation, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task ExecuteSynchronousAsync(Action operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(operation, CancellationToken.None).ConfigureAwait(false);
    }
}
