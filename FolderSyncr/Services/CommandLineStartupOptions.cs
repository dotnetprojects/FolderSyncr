namespace FolderSyncr.Services;

public sealed record CommandLineStartupOptions(
    string? ConfigurationPath,
    string? OverrideLeftPath,
    string? OverrideRightPath);
