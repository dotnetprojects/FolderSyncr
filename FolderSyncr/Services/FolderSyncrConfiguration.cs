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
    SyncErrorHandling ErrorHandling,
    SymbolicLinkHandling SymbolicLinkHandling,
    string IncludePatterns,
    string ExcludePatterns,
    bool IsDarkMode,
    IReadOnlyList<ExternalCommandDefinition>? ExternalCommands = null,
    IReadOnlyList<FolderPairConfiguration>? FolderPairs = null,
    CustomSyncRules? CustomRules = null);
