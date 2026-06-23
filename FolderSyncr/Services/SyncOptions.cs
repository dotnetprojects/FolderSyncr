using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed class SyncOptions
{
    public required string LeftPath { get; init; }
    public required string RightPath { get; init; }
    public SyncMode Mode { get; init; }
    public CompareMethod CompareMethod { get; init; }
    public int FileTimeToleranceSeconds { get; init; } = 2;
    public bool IgnoreDaylightSavingTimeShift { get; init; }
    public bool VerifyCopiedFiles { get; init; }
    public DeletionHandling DeletionHandling { get; init; } = DeletionHandling.Permanent;
    public VersioningMode VersioningMode { get; init; } = VersioningMode.TimeStampFolder;
    public string VersioningFolderPath { get; init; } = string.Empty;
    public string IncludePatterns { get; init; } = "*";
    public string ExcludePatterns { get; init; } = string.Empty;
    public bool DryRun { get; init; } = true;
}
