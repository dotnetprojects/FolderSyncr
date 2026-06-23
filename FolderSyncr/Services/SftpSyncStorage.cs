using System.IO;
using FolderSyncr.Models;
using Renci.SshNet;

namespace FolderSyncr.Services;

internal sealed class SftpSyncStorage(RemoteSyncRoot root) : ISyncStorage
{
    public string Root => root.Uri.ToString();

    public bool IsRemote => true;

    public async Task<IReadOnlyDictionary<string, FileSnapshot>> ScanAsync(
        CompareMethod compareMethod,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var client = CreateConnectedClient(options);
        var filter = new FileFilter(options.IncludePatterns, options.ExcludePatterns);
        var files = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        await ScanDirectoryAsync(client, root.RootPath, compareMethod, filter, files, progress, cancellationToken);
        progress?.Report($"Found {files.Count:n0} file(s) in {Root}");
        return files;
    }

    public async Task<Stream> OpenReadAsync(FileSnapshot snapshot, SyncOptions options, CancellationToken cancellationToken)
    {
        using var client = CreateConnectedClient(options);
        var memory = new MemoryStream();
        await client.DownloadFileAsync(snapshot.FullPath, memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    public async Task WriteFileAsync(
        string relativePath,
        Stream content,
        DateTime lastWriteTimeUtc,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        using var client = CreateConnectedClient(options);
        var destinationPath = root.Combine(relativePath);
        await EnsureDirectoryAsync(client, GetRemoteDirectory(destinationPath), cancellationToken);
        await client.UploadFileAsync(content, destinationPath, cancellationToken);
        client.SetLastWriteTimeUtc(destinationPath, lastWriteTimeUtc);
    }

    public async Task DeleteFileAsync(FileSnapshot snapshot, SyncOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (options.DeletionHandling != DeletionHandling.Permanent)
        {
            throw new NotSupportedException("Remote SFTP deletions currently support permanent delete only.");
        }

        progress?.Report($"Delete {snapshot.RelativePath}");
        using var client = CreateConnectedClient(options);
        await client.DeleteFileAsync(snapshot.FullPath, cancellationToken);
    }

    private async Task ScanDirectoryAsync(
        SftpClient client,
        string directory,
        CompareMethod compareMethod,
        FileFilter filter,
        Dictionary<string, FileSnapshot> files,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await foreach (var item in client.ListDirectoryAsync(directory, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Name is "." or "..")
            {
                continue;
            }

            if (item.IsDirectory)
            {
                await ScanDirectoryAsync(client, item.FullName, compareMethod, filter, files, progress, cancellationToken);
                continue;
            }

            if (!item.IsRegularFile)
            {
                continue;
            }

            if (SyncDatabaseStore.IsDatabaseFile(item.Name))
            {
                continue;
            }

            var relativePath = root.GetRelativePath(item.FullName);
            if (!filter.IsMatch(relativePath))
            {
                continue;
            }

            string? hash = null;
            if (compareMethod == CompareMethod.ContentHash)
            {
                using var memory = new MemoryStream();
                await client.DownloadFileAsync(item.FullName, memory, cancellationToken);
                memory.Position = 0;
                hash = await LocalSyncStorage.HashStreamAsync(memory, cancellationToken);
            }

            files[relativePath] = new FileSnapshot(
                Root,
                relativePath,
                item.FullName,
                item.Length,
                item.LastWriteTimeUtc,
                hash);

            if (files.Count % 250 == 0)
            {
                progress?.Report($"Scanned {files.Count:n0} file(s) in {Root}");
            }
        }
    }

    private SftpClient CreateConnectedClient(SyncOptions options)
    {
        if (string.IsNullOrWhiteSpace(root.Username) || string.IsNullOrEmpty(root.Password))
        {
            throw new InvalidOperationException("SFTP paths must include credentials, for example sftp://user:password@example.com/backups.");
        }

        var connectionInfo = new PasswordConnectionInfo(root.Host, root.Port, root.Username, root.Password);
        if (options.SftpCompression
            && connectionInfo.CompressionAlgorithms.TryGetValue("zlib@openssh.com", out var zlibOpenSshFactory))
        {
            connectionInfo.CompressionAlgorithms.TryGetValue("none", out var noneFactory);
            connectionInfo.CompressionAlgorithms.Clear();
            connectionInfo.CompressionAlgorithms["zlib@openssh.com"] = zlibOpenSshFactory;
            connectionInfo.CompressionAlgorithms["none"] = noneFactory;
        }

        var client = new SftpClient(connectionInfo);
        client.Connect();
        return client;
    }

    private static async Task EnsureDirectoryAsync(SftpClient client, string directory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory == "/")
        {
            return;
        }

        var current = directory.StartsWith('/') ? "/" : string.Empty;
        foreach (var segment in directory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current == "/" ? $"/{segment}" : RemoteSyncRoot.CombineRemotePath(current, segment);
            if (!await client.ExistsAsync(current, cancellationToken))
            {
                await client.CreateDirectoryAsync(current, cancellationToken);
            }
        }
    }

    private static string GetRemoteDirectory(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }
}
