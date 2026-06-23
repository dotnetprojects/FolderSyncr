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
    IReadOnlyDictionary<string, string>? TemporaryVariables = null)
{
    public BatchRunOptions(
        string ConfigurationPath,
        string? OverrideLeftPath,
        string? OverrideRightPath,
        bool DryRun,
        string? JsonOutputPath,
        SyncErrorHandling? ErrorHandling = null,
        SymbolicLinkHandling? SymbolicLinkHandling = null,
        IReadOnlyDictionary<string, string>? TemporaryVariables = null)
        : this(
            [ConfigurationPath],
            OverrideLeftPath,
            OverrideRightPath,
            DryRun,
            JsonOutputPath,
            ErrorHandling,
            SymbolicLinkHandling,
            TemporaryVariables)
    {
    }

    public string ConfigurationPath => ConfigurationPaths.FirstOrDefault() ?? string.Empty;
}
