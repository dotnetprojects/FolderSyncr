namespace FolderSyncr.ViewModels;

public sealed class OverviewItem
{
    public required string Folder { get; init; }
    public int Items { get; init; }
    public string Size { get; init; } = "0 B";
    public double Percentage { get; init; }
}
