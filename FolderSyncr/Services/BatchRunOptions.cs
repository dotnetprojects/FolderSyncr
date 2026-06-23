using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed record BatchRunOptions(
    IReadOnlyList<string> ConfigurationPaths,
    string? OverrideLeftPath,
    string? OverrideRightPath,
    bool DryRun,
    string? JsonOutputPath,
    SyncErrorHandling? ErrorHandling = null,
    SymbolicLinkHandling? SymbolicLinkHandling = null,
    IReadOnlyDictionary<string, string>? TemporaryVariables = null,
    bool Watch = false,
    TimeSpan? WatchIdleDelay = null)
{
    public BatchRunOptions(
        string ConfigurationPath,
        string? OverrideLeftPath,
        string? OverrideRightPath,
        bool DryRun,
        string? JsonOutputPath,
        SyncErrorHandling? ErrorHandling = null,
        SymbolicLinkHandling? SymbolicLinkHandling = null,
        IReadOnlyDictionary<string, string>? TemporaryVariables = null,
        bool Watch = false,
        TimeSpan? WatchIdleDelay = null)
        : this(
            [ConfigurationPath],
            OverrideLeftPath,
            OverrideRightPath,
            DryRun,
            JsonOutputPath,
            ErrorHandling,
            SymbolicLinkHandling,
            TemporaryVariables,
            Watch,
            WatchIdleDelay)
    {
    }

    public string ConfigurationPath => ConfigurationPaths.FirstOrDefault() ?? string.Empty;
}
