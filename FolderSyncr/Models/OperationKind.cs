namespace FolderSyncr.Models;

public enum OperationKind
{
    Equal,
    CopyLeftToRight,
    CopyRightToLeft,
    DeleteLeft,
    DeleteRight,
    Conflict
}
