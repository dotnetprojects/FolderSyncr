namespace FolderSyncr.Models;

public sealed record CustomSyncRules(
    CustomSyncAction LeftOnly,
    CustomSyncAction RightOnly,
    CustomSyncAction LeftNewer,
    CustomSyncAction RightNewer,
    CustomSyncAction Different)
{
    public static CustomSyncRules Default { get; } = new(
        CustomSyncAction.CopyLeftToRight,
        CustomSyncAction.CopyRightToLeft,
        CustomSyncAction.CopyLeftToRight,
        CustomSyncAction.CopyRightToLeft,
        CustomSyncAction.DoNothing);

    public CustomSyncAction CreatedLeft { get; init; } = LeftOnly;
    public CustomSyncAction CreatedRight { get; init; } = RightOnly;
    public CustomSyncAction UpdatedLeft { get; init; } = LeftNewer;
    public CustomSyncAction UpdatedRight { get; init; } = RightNewer;
    public CustomSyncAction DeletedLeft { get; init; } = CustomSyncAction.DeleteRight;
    public CustomSyncAction DeletedRight { get; init; } = CustomSyncAction.DeleteLeft;
}
