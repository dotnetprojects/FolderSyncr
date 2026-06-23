using System.IO;
using FluentFTP;
using FolderSyncr.Models;

namespace FolderSyncr.Services;

internal sealed class FtpSyncStorage(RemoteSyncRoot root) : ISyncStorage
{
    public string Root => root.Uri.ToString();

    public bool IsRemote => true;

    public async Task<IReadOnlyDictionary<string, FileSnapshot>> ScanAsync(
        CompareMethod compareMethod,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var client = await CreateConnectedClientAsync(cancellationToken);
        var filter = new FileFilter(options.IncludePatterns, options.ExcludePatterns);
        var files = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        await ScanDirectoryAsync(client, root.RootPath, compareMethod, filter, files, progress, cancellationToken);
        progress?.Report($"Found {files.Count:n0} file(s) in {Root}");
        return files;
    }

    public async Task<Stream> OpenReadAsync(FileSnapshot snapshot, SyncOptions options, CancellationToken cancellationToken)
    {
        using var client = await CreateConnectedClientAsync(cancellationToken);
        var memory = new MemoryStream();
        var downloaded = await client.DownloadStream(memory, snapshot.FullPath, token: cancellationToken);
        if (!downloaded)
        {
            throw new IOException($"FTP download failed for {snapshot.FullPath}.");
        }

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
        using var client = await CreateConnectedClientAsync(cancellationToken);
        var destinationPath = root.Combine(relativePath);
        var status = await client.UploadStream(
            content,
            destinationPath,
            FtpRemoteExists.Overwrite,
            createRemoteDir: true,
            token: cancellationToken);
        if (status != FtpStatus.Success)
        {
            throw new IOException($"FTP upload failed for {destinationPath}: {status}");
        }

        await client.SetModifiedTime(destinationPath, lastWriteTimeUtc, cancellationToken);
    }

    public async Task DeleteFileAsync(FileSnapshot snapshot, SyncOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (options.DeletionHandling != DeletionHandling.Permanent)
        {
            throw new NotSupportedException("Remote FTP deletions currently support permanent delete only.");
        }

        progress?.Report($"Delete {snapshot.RelativePath}");
        using var client = await CreateConnectedClientAsync(cancellationToken);
        await client.DeleteFile(snapshot.FullPath, cancellationToken);
    }

    private async Task ScanDirectoryAsync(
        AsyncFtpClient client,
        string directory,
        CompareMethod compareMethod,
        FileFilter filter,
        Dictionary<string, FileSnapshot> files,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var items = await client.GetListing(directory, FtpListOption.Auto, cancellationToken);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Type == FtpObjectType.Directory)
            {
                await ScanDirectoryAsync(client, item.FullName, compareMethod, filter, files, progress, cancellationToken);
                continue;
            }

            if (item.Type != FtpObjectType.File || SyncDatabaseStore.IsDatabaseFile(item.Name))
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
                var downloaded = await client.DownloadStream(memory, item.FullName, token: cancellationToken);
                if (!downloaded)
                {
                    throw new IOException($"FTP download failed for hashing {item.FullName}.");
                }

                memory.Position = 0;
                hash = await LocalSyncStorage.HashStreamAsync(memory, cancellationToken);
            }

            files[relativePath] = new FileSnapshot(
                Root,
                relativePath,
                item.FullName,
                item.Size,
                item.Modified.ToUniversalTime(),
                hash);

            if (files.Count % 250 == 0)
            {
                progress?.Report($"Scanned {files.Count:n0} file(s) in {Root}");
            }
        }
    }

    private async Task<AsyncFtpClient> CreateConnectedClientAsync(CancellationToken cancellationToken)
    {
        var username = string.IsNullOrWhiteSpace(root.Username) ? "anonymous" : root.Username;
        var password = string.IsNullOrEmpty(root.Password) ? "anonymous@foldersyncr.local" : root.Password;
        var client = new AsyncFtpClient(root.Host, username, password, root.Port);
        await client.Connect(cancellationToken);
        return client;
    }
}
