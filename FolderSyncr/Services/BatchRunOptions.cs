using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed record BatchRunOptions(
    string ConfigurationPath,
    string? OverrideLeftPath,
    string? OverrideRightPath,
    bool DryRun,
    string? JsonOutputPath,
    SyncErrorHandling? ErrorHandling = null);
