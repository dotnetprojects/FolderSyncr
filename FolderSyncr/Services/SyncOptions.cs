using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed class SyncOptions
{
    public required string LeftPath { get; init; }
    public required string RightPath { get; init; }
    public SyncMode Mode { get; init; }
    public CompareMethod CompareMethod { get; init; }
    public string IncludePatterns { get; init; } = "*";
    public string ExcludePatterns { get; init; } = string.Empty;
    public bool DryRun { get; init; } = true;
}
