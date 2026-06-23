using System.IO;
using System.Security.Cryptography;
using FolderSyncr.Models;
using RecycleOption = Microsoft.VisualBasic.FileIO.RecycleOption;
using UIOption = Microsoft.VisualBasic.FileIO.UIOption;
using VisualBasicFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;

namespace FolderSyncr.Services;

internal sealed class LocalSyncStorage(string root) : ISyncStorage
{
    private const string LockFileName = ".foldersyncr.lock";

    public string Root { get; } = root;

    public bool IsRemote => false;

    public async Task<IReadOnlyDictionary<string, FileSnapshot>> ScanAsync(
        CompareMethod compareMethod,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var filter = new FileFilter(options.IncludePatterns, options.ExcludePatterns);
        var files = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateFilePaths(Root, options.SymbolicLinkHandling))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsInternalMetadataFile(path))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(Root, path);
            if (!filter.IsMatch(relativePath))
            {
                continue;
            }

            var info = new FileInfo(path);
            var isSymbolicLink = info.LinkTarget is not null;
            if (isSymbolicLink && options.SymbolicLinkHandling == SymbolicLinkHandling.Skip)
            {
                continue;
            }

            string? hash = null;
            if (isSymbolicLink && options.SymbolicLinkHandling == SymbolicLinkHandling.CopyLinksAsLinks)
            {
                hash = info.LinkTarget;
            }
            else if (compareMethod == CompareMethod.ContentHash)
            {
                await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                hash = await HashStreamAsync(stream, cancellationToken);
            }

            files[relativePath] = new FileSnapshot(
                Root,
                relativePath,
                path,
                info.Length,
                info.LastWriteTimeUtc,
                hash,
                isSymbolicLink && options.SymbolicLinkHandling == SymbolicLinkHandling.CopyLinksAsLinks,
                isSymbolicLink && options.SymbolicLinkHandling == SymbolicLinkHandling.CopyLinksAsLinks ? info.LinkTarget : null);

            if (files.Count % 250 == 0)
            {
                progress?.Report($"Scanned {files.Count:n0} file(s) in {Root}");
            }
        }

        progress?.Report($"Found {files.Count:n0} file(s) in {Root}");
        return files;
    }

    public Task<Stream> OpenReadAsync(FileSnapshot snapshot, SyncOptions options, CancellationToken cancellationToken)
    {
        Stream stream = File.Open(snapshot.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public async Task WriteFileAsync(
        string relativePath,
        Stream content,
        DateTime lastWriteTimeUtc,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var destinationPath = Path.Combine(Root, relativePath);
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using (var output = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(output, cancellationToken);
        }

        File.SetLastWriteTimeUtc(destinationPath, lastWriteTimeUtc);
    }

    public Task DeleteFileAsync(FileSnapshot snapshot, SyncOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report($"Delete {snapshot.RelativePath}");
        switch (options.DeletionHandling)
        {
            case DeletionHandling.RecycleBin:
                VisualBasicFileSystem.DeleteFile(snapshot.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                break;
            case DeletionHandling.VersioningFolder:
                MoveToVersioningFolder(snapshot, options);
                break;
            default:
                File.Delete(snapshot.FullPath);
                break;
        }

        RemoveEmptyParents(Path.GetDirectoryName(snapshot.FullPath));
        return Task.CompletedTask;
    }

    public static async Task<string> HashStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public static bool IsInternalMetadataFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, LockFileName, StringComparison.OrdinalIgnoreCase)
            || SyncDatabaseStore.IsDatabaseFile(path);
    }

    private static IEnumerable<string> EnumerateFilePaths(string root, SymbolicLinkHandling symbolicLinkHandling)
    {
        foreach (var file in Directory.EnumerateFiles(root))
        {
            yield return file;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var isSymbolicDirectory = File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);
            if (isSymbolicDirectory && symbolicLinkHandling != SymbolicLinkHandling.Follow)
            {
                continue;
            }

            foreach (var file in EnumerateFilePaths(directory, symbolicLinkHandling))
            {
                yield return file;
            }
        }
    }

    private static void MoveToVersioningFolder(FileSnapshot file, SyncOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.VersioningFolderPath))
        {
            throw new InvalidOperationException("Choose a versioning folder before using versioning deletion handling.");
        }

        var versionRoot = PathMacroExpander.Expand(options.VersioningFolderPath);
        var destinationPath = GetVersioningDestinationPath(file, versionRoot, options.VersioningMode);
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destinationPath))
        {
            if (options.VersioningMode == VersioningMode.Replace)
            {
                File.Delete(destinationPath);
            }
            else
            {
                destinationPath = GetUniquePath(destinationPath);
            }
        }

        File.Move(file.FullPath, destinationPath);
    }

    private static string GetVersioningDestinationPath(FileSnapshot file, string versionRoot, VersioningMode versioningMode)
    {
        return versioningMode switch
        {
            VersioningMode.Replace => Path.Combine(versionRoot, file.RelativePath),
            VersioningMode.FileTime => Path.Combine(
                versionRoot,
                AppendTimestampToFileName(file.RelativePath, file.LastWriteTimeUtc)),
            _ => Path.Combine(
                versionRoot,
                DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture),
                file.RelativePath)
        };
    }

    private static string AppendTimestampToFileName(string relativePath, DateTime lastWriteTimeUtc)
    {
        var directory = Path.GetDirectoryName(relativePath);
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var extension = Path.GetExtension(relativePath);
        var timestamp = lastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var stampedName = $"{fileName} {timestamp}{extension}";
        return string.IsNullOrWhiteSpace(directory) ? stampedName : Path.Combine(directory, stampedName);
    }

    private static string GetUniquePath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void RemoveEmptyParents(string? directory)
    {
        while (!string.IsNullOrEmpty(directory) && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
