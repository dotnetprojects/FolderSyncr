namespace FolderSyncr.Models;

public sealed record FileSnapshot(
    string Root,
    string RelativePath,
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc,
    string? Hash);
