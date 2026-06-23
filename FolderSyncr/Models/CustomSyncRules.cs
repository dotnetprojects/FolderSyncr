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
}
