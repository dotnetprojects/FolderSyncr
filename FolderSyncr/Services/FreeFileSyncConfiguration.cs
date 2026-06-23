using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed record FreeFileSyncConfiguration(
    string SourcePath,
    IReadOnlyList<FreeFileSyncFolderPair> FolderPairs,
    SyncMode? SyncMode,
    CompareMethod? CompareMethod,
    string IncludePatterns,
    string ExcludePatterns,
    IReadOnlyList<string> Warnings);

public sealed record FreeFileSyncFolderPair(string LeftPath, string RightPath);
