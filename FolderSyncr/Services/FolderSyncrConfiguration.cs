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
    bool IgnoreDaylightSavingTimeShift,
    bool VerifyCopiedFiles,
    DeletionHandling DeletionHandling,
    VersioningMode VersioningMode,
    string VersioningFolderPath,
    string IncludePatterns,
    string ExcludePatterns,
    bool IsDarkMode);
