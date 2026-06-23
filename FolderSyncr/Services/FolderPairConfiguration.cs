namespace FolderSyncr.Services;

public sealed record FolderPairConfiguration(
    string LeftPath,
    string RightPath,
    string? IncludePatterns = null,
    string? ExcludePatterns = null);
