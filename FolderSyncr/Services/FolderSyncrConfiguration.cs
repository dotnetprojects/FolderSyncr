using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed record FolderSyncrConfiguration(
    int Version,
    string Name,
    string LeftPath,
    string RightPath,
    SyncMode SyncMode,
    CompareMethod CompareMethod,
    int FileTimeToleranceSeconds,
    bool VerifyCopiedFiles,
    string IncludePatterns,
    string ExcludePatterns,
    bool IsDarkMode);
