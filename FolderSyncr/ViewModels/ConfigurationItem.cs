namespace FolderSyncr.ViewModels;

public sealed class ConfigurationItem
{
    public required string Name { get; init; }
    public string LastSync { get; init; } = "Never";
    public bool IsHealthy { get; init; } = true;
}
