using System.IO;
using FolderSyncr.Models;

namespace FolderSyncr.Services;

internal interface ISyncStorage
{
    string Root { get; }
    bool IsRemote { get; }

    Task<IReadOnlyDictionary<string, FileSnapshot>> ScanAsync(
        CompareMethod compareMethod,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(FileSnapshot snapshot, SyncOptions options, CancellationToken cancellationToken);

    Task WriteFileAsync(
        string relativePath,
        Stream content,
        DateTime lastWriteTimeUtc,
        SyncOptions options,
        CancellationToken cancellationToken);

    Task DeleteFileAsync(FileSnapshot snapshot, SyncOptions options, IProgress<string>? progress, CancellationToken cancellationToken);
}
